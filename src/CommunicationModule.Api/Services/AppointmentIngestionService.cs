using CommunicationModule.Core.Enums;
using CommunicationModule.Core.Models;
using CommunicationModule.Infrastructure.Data;
using CommunicationModule.Infrastructure.Services;
using Hangfire;
using FhirAppointment = Hl7.Fhir.Model.Appointment;
using DomainAppointment = CommunicationModule.Core.Models.Appointment;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace CommunicationModule.Api.Services;

public class AppointmentIngestionService
{
    private readonly CommunicationDbContext _db;
    private readonly AesEncryptionService _encryption;
    private readonly IBackgroundJobClient _jobs;
    private readonly CommunicationModule.Core.Interfaces.IEventPublisher _events;

    public AppointmentIngestionService(
        CommunicationDbContext db,
        AesEncryptionService encryption,
        IBackgroundJobClient jobs,
        CommunicationModule.Core.Interfaces.IEventPublisher events)
    {
        _db = db;
        _encryption = encryption;
        _jobs = jobs;
        _events = events;
    }

    public async Task<IngestionResult> IngestAsync(
        string fhirJson,
        Guid organisationId,
        CancellationToken ct)
    {
        var ingestStart = Stopwatch.GetTimestamp();

        FhirAppointment fhir;

        try
        {
            var deserializer = new FhirJsonDeserializer(
                new DeserializerSettings { AcceptUnknownMembers = false });

            fhir = deserializer.Deserialize<FhirAppointment>(fhirJson);
        }
        catch (Exception ex)
        {
            return IngestionResult.Fail($"Invalid FHIR Appointment: {ex.Message}");
        }

        var validationError = ValidateAppointment(fhir);
        if (validationError is not null)
            return IngestionResult.Fail(validationError);

        if (fhir.Start is null)
            return IngestionResult.Fail("Appointment.start is required.");

        var appointmentTime = fhir.Start.Value.UtcDateTime;
        var status = MapStatus(fhir.Status);

        var location = ExtractLocation(fhir);
        var patientPhone = ExtractPatientPhone(fhir);

        if (string.IsNullOrWhiteSpace(patientPhone))
            return IngestionResult.Fail("Patient.telecom must contain a phone number.");

        var encryptedPhone = _encryption.Encrypt(patientPhone);

        var appointment = await _db.Appointments
            .Include(a => a.NotificationJobs)
            .FirstOrDefaultAsync(
                a => a.FhirAppointmentId == fhir.Id &&
                     a.OrganisationId == organisationId,
                ct);

        var isNew = appointment is null;

        if (isNew)
        {
            appointment = new DomainAppointment
            {
                Id = Guid.NewGuid(),
                FhirAppointmentId = fhir.Id ?? string.Empty,
                OrganisationId = organisationId
            };

            _db.Appointments.Add(appointment);
        }

        appointment.EncryptedPatientPhone = encryptedPhone;
        appointment.AppointmentDateTime = appointmentTime;
        appointment.Location = location;
        appointment.Instructions = fhir.Description;
        appointment.Status = status;

        await _db.SaveChangesAsync(ct);

        // =========================
        // Jobs scheduling (only for scheduled)
        // =========================
        if (status == AppointmentStatus.Scheduled)
        {
            var now = DateTime.UtcNow;

            var t24 = appointmentTime.AddHours(-24);
            var t1 = appointmentTime.AddHours(-1);

            if (t24 > now)
            {
                var job = new NotificationJob
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = appointment.Id,
                    Type = NotificationJobType.TwentyFourHour,
                    ScheduledFor = t24
                };

                _db.NotificationJobs.Add(job);

                _jobs.Schedule<NotificationDispatchService>(
                    s => s.DispatchAsync(job.Id, CancellationToken.None),
                    t24 - now);
            }

            if (t1 > now)
            {
                var job = new NotificationJob
                {
                    Id = Guid.NewGuid(),
                    AppointmentId = appointment.Id,
                    Type = NotificationJobType.OneHour,
                    ScheduledFor = t1
                };

                _db.NotificationJobs.Add(job);

                _jobs.Schedule<NotificationDispatchService>(
                    s => s.DispatchAsync(job.Id, CancellationToken.None),
                    t1 - now);
            }

            await _db.SaveChangesAsync(ct);
        }

        await _events.PublishAsync(
            new CommunicationModule.Core.Events.AppointmentReceivedEvent(
                appointment.Id,
                organisationId,
                appointmentTime,
                location),
            ct);

        return IngestionResult.Ok(appointment.Id, scheduled: isNew);
    }

    // =========================
    // VALIDATION
    // =========================
    private static string? ValidateAppointment(FhirAppointment fhir)
    {
        if (string.IsNullOrWhiteSpace(fhir.Id))
            return "Appointment.id is required.";

        if (fhir.Start is null)
            return "Appointment.start is required.";

        if (fhir.Status is null)
            return "Appointment.status is required.";

        var patientParticipant =
            fhir.Participant.FirstOrDefault(p =>
                p.Actor?.Reference?.StartsWith("Patient/") == true);

        if (patientParticipant?.Actor?.Reference is null)
            return "Appointment must contain a Patient reference.";

        var patientId = patientParticipant.Actor.Reference
            .Replace("Patient/", "")
            .TrimStart('#');

        var patient = fhir.Contained
            .OfType<Patient>()
            .FirstOrDefault(p => p.Id == patientId);

        if (patient is null)
            return "Contained Patient resource is missing.";

        if (patient.Telecom is null ||
            !patient.Telecom.Any(t =>
                t.System == ContactPoint.ContactPointSystem.Phone &&
                !string.IsNullOrWhiteSpace(t.Value)))
            return "Contained Patient must include a phone number.";

        return null;
    }

    // =========================
    // HELPERS
    // =========================
    private static string ExtractLocation(FhirAppointment fhir)
        => fhir.Participant
            .FirstOrDefault(p => p.Actor?.Reference?.StartsWith("Location/") == true)
            ?.Actor?.Display ?? "Unknown location";

    private static string? ExtractPatientPhone(FhirAppointment fhir)
    {
        var patientRef = fhir.Participant
            .FirstOrDefault(p => p.Actor?.Reference?.StartsWith("Patient/") == true)
            ?.Actor?.Reference;

        if (patientRef is null) return null;

        var patientId = patientRef.Replace("Patient/", "").TrimStart('#');

        var patient = fhir.Contained
            .OfType<Patient>()
            .FirstOrDefault(p => p.Id == patientId);

        return patient?.Telecom?
            .FirstOrDefault(t =>
                t.System == ContactPoint.ContactPointSystem.Phone)
            ?.Value;
    }

    private static AppointmentStatus MapStatus(FhirAppointment.AppointmentStatus? status) =>
        status switch
        {
            FhirAppointment.AppointmentStatus.Booked => AppointmentStatus.Scheduled,
            FhirAppointment.AppointmentStatus.Cancelled => AppointmentStatus.Cancelled,
            FhirAppointment.AppointmentStatus.Fulfilled => AppointmentStatus.Completed,
            _ => AppointmentStatus.Scheduled
        };
}

public record IngestionResult(bool Success, Guid? AppointmentId, bool Scheduled, string? Error)
{
    public static IngestionResult Ok(Guid id, bool scheduled) => new(true, id, scheduled, null);
    public static IngestionResult Fail(string error) => new(false, null, false, error);
}