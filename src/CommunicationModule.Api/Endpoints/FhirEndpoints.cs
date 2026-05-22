using CommunicationModule.Api.Services;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

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
            AppointmentIngestionService ingestion,
            CancellationToken ct) =>
        {
            if (!request.Headers.TryGetValue("X-Organisation-Id", out var orgHeader)
                || !Guid.TryParse(orgHeader, out var organisationId))
            {
                return BuildOutcome(
                    OperationOutcome.IssueSeverity.Error,
                    "X-Organisation-Id header is required and must be a valid GUID.");
            }

            string fhirJson;
            using (var reader = new StreamReader(request.Body))
                fhirJson = await reader.ReadToEndAsync(ct);

            if (string.IsNullOrWhiteSpace(fhirJson))
                return BuildOutcome(OperationOutcome.IssueSeverity.Error, "Request body is empty.");

            var result = await ingestion.IngestAsync(fhirJson, organisationId, ct);

            if (!result.Success)
                return BuildOutcome(OperationOutcome.IssueSeverity.Error, result.Error!);

            var message = result.Scheduled
                ? $"Appointment {result.AppointmentId} received and notifications scheduled."
                : $"Appointment {result.AppointmentId} updated — no new jobs scheduled.";

            return BuildOutcome(OperationOutcome.IssueSeverity.Information, message, StatusCodes.Status200OK);
        })
        .Accepts<string>("application/fhir+json", "application/json")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        return app;
    }

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
