using CommunicationModule.Api.Services;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.EntityFrameworkCore;

namespace CommunicationModule.Api.Endpoints;

public static class FhirEndpoints
{
    public static IEndpointRouteBuilder MapFhirEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/fhir");

        // POST /fhir/appointments
        // Header: X-Organisation-Id (Guid) — identifies which tenant is sending this
        // Body:   FHIR R4 Appointment resource (application/fhir+json)
        //
        // Sample Postman body — paste into Body > raw > JSON:
        // {
        //   "resourceType": "Appointment",
        //   "id": "apt-001",
        //   "status": "booked",
        //   "start": "2026-06-01T10:00:00+00:00",
        //   "description": "Please fast for 8 hours before your appointment.",
        //   "participant": [
        //     { "actor": { "reference": "#pat-001" }, "status": "accepted" },
        //     { "actor": { "reference": "Location/loc-001", "display": "Room 3, Cardiology" }, "status": "accepted" }
        //   ],
        //   "contained": [
        //     {
        //       "resourceType": "Patient",
        //       "id": "pat-001",
        //       "telecom": [{ "system": "phone", "value": "+31612345678" }]
        //     }
        //   ]
        // }
        group.MapPost("/appointments", async (
            HttpRequest request,
            CommunicationModule.Infrastructure.Data.CommunicationDbContext db,
            TenantAccessService accessService,
            AppointmentIngestionService ingestion,
            CancellationToken ct) =>
        {
            if (!request.Headers.TryGetValue("X-OpenMRS-Instance-Id", out var instanceHeader)
                || !Guid.TryParse(instanceHeader, out var instanceId))
            {
                return BuildNak(
                    OperationOutcome.IssueSeverity.Error,
                    "X-OpenMRS-Instance-Id header is required and must be a valid GUID.");
            }

            if (!request.Headers.TryGetValue("X-OpenMRS-Access-Key", out var accessKeyHeader)
                || string.IsNullOrWhiteSpace(accessKeyHeader))
            {
                return BuildNak(
                    OperationOutcome.IssueSeverity.Error,
                    "X-OpenMRS-Access-Key header is required.");
            }

            var instance = await db.OpenMRSInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == instanceId && i.IsActive, ct);

            if (instance is null)
            {
                return BuildNak(
                    OperationOutcome.IssueSeverity.Error,
                    "Unknown OpenMRS instance.",
                    StatusCodes.Status404NotFound);
            }

            if (!accessService.Matches(accessKeyHeader.ToString(), instance.AccessKeyHash))
            {
                return BuildNak(
                    OperationOutcome.IssueSeverity.Error,
                    "Invalid OpenMRS access key.",
                    StatusCodes.Status401Unauthorized);
            }

            string fhirJson;
            using (var reader = new StreamReader(request.Body))
                fhirJson = await reader.ReadToEndAsync(ct);

            if (string.IsNullOrWhiteSpace(fhirJson))
                return BuildNak(OperationOutcome.IssueSeverity.Error, "Request body is empty.");

            if (!fhirJson.TrimStart().StartsWith('{'))
                return BuildNak(OperationOutcome.IssueSeverity.Error, "Request body must be valid FHIR JSON.");

            var result = await ingestion.IngestAsync(fhirJson, instance.OrganisationId, ct);

            if (!result.Success)
                return BuildNak(OperationOutcome.IssueSeverity.Error, result.Error!);

            var message = result.Scheduled
                ? $"Appointment {result.AppointmentId} accepted and queued for processing."
                : $"Appointment {result.AppointmentId} accepted and updated.";

            return BuildAck(message, StatusCodes.Status202Accepted);
        })
        .Accepts<string>("application/fhir+json", "application/json")
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status400BadRequest);

        return app;
    }

    private static IResult BuildAck(string message, int statusCode = StatusCodes.Status202Accepted)
        => BuildOutcome(OperationOutcome.IssueSeverity.Information, message, statusCode);

    private static IResult BuildNak(
        OperationOutcome.IssueSeverity severity,
        string message,
        int statusCode = StatusCodes.Status400BadRequest)
        => BuildOutcome(severity, message, statusCode);

    private static IResult BuildOutcome(
        OperationOutcome.IssueSeverity severity,
        string message,
        int statusCode = StatusCodes.Status400BadRequest)
    {
        var outcome = new OperationOutcome
        {
            Issue =
            [
                new OperationOutcome.IssueComponent
                {
                    Severity = severity,
                    Code = severity == OperationOutcome.IssueSeverity.Information
                        ? OperationOutcome.IssueType.Informational
                        : OperationOutcome.IssueType.Invalid,
                    Diagnostics = message
                }
            ]
        };

        var serializer = new FhirJsonSerializer();
        return Results.Content(serializer.SerializeToString(outcome), "application/fhir+json", statusCode: statusCode);
    }
}
