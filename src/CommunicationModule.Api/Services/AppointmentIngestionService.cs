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
using Task = System.Threading.Tasks.Task;

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
        var deserializer = new FhirJsonDeserializer(
            new DeserializerSettings { AcceptUnknownMembers = false });

        FhirAppointment fhir;

        try
        {
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

        // 🔥 LOAD WITH TRACKING (IMPORTANT FOR CONSISTENCY)
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
        // IDEMPOTENT SCHEDULING
        // =========================
        if (status == AppointmentStatus.Scheduled)
        {
            await ScheduleIfNotExistsAsync(
                appointment,
                NotificationJobType.TwentyFourHour,
                appointmentTime.AddHours(-24),
                ct);

            await ScheduleIfNotExistsAsync(
                appointment,
                NotificationJobType.OneHour,
                appointmentTime.AddHours(-1),
                ct);

            await _db.SaveChangesAsync(ct);
        }

        await _events.PublishAsync(
            new CommunicationModule.Core.Events.AppointmentReceivedEvent(
                appointment.Id,
                organisationId,
                appointmentTime,
                location),
            ct);

        return IngestionResult.Ok(appointment.Id, scheduled: true);
    }

    private async Task ScheduleIfNotExistsAsync(
    DomainAppointment appointment,
    NotificationJobType type,
    DateTime scheduledFor,
    CancellationToken ct)
{
    if (scheduledFor <= DateTime.UtcNow)
        return;

    var exists = await _db.NotificationJobs.AnyAsync(j =>
        j.AppointmentId == appointment.Id &&
        j.Type == type,
        ct);

    if (exists)
        return;

    var job = new NotificationJob
    {
        Id = Guid.NewGuid(),
        AppointmentId = appointment.Id,
        Type = type,
        ScheduledFor = scheduledFor,
        Status = NotificationJobStatus.Pending
    };

    _db.NotificationJobs.Add(job);

    _jobs.Schedule<NotificationDispatchService>(
        s => s.DispatchAsync(job.Id, CancellationToken.None),
        scheduledFor - DateTime.UtcNow);

    await Task.CompletedTask; // 🔥 FIX COMPILER EDGE CASE
}

    // =========================
    // VALIDATION (UNCHANGED)
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