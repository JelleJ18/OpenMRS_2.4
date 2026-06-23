namespace CommunicationModule.Api.Endpoints;
using CommunicationModule.Api.Services;
using System.Diagnostics;

public static class Hl7Endpoints
{
    public static IEndpointRouteBuilder MapHl7Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/hl7");

        group.MapPost("/", async (
            HttpRequest request,
            AppointmentIngestionService ingestion,
            CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var hl7 = await reader.ReadToEndAsync(ct);

            if (string.IsNullOrWhiteSpace(hl7))
                return Results.BadRequest("Empty HL7 message");

            Telemetry.Hl7MessagesReceived.Add(1);
            BusinessMetrics.IncrementHl7Received();

            // Parse HL7
            var parseStart = Stopwatch.GetTimestamp();
            var parsed = Hl7Parser.Parse(hl7);
            var parseDuration = Stopwatch.GetElapsedTime(parseStart).TotalSeconds;
            Telemetry.Hl7ParseDuration.Record(parseDuration);
            BusinessMetrics.RecordHl7ParseDuration(parseDuration);

            // Map naar FHIR (jouw bestaande flow)
            var mappingStart = Stopwatch.GetTimestamp();
            var fhirJson = Hl7ToFhirMapper.MapToAppointment(parsed);
            var mappingDuration = Stopwatch.GetElapsedTime(mappingStart).TotalSeconds;
            Telemetry.Hl7MappingDuration.Record(mappingDuration);
            BusinessMetrics.RecordHl7MappingDuration(mappingDuration);

            // Gebruik jouw bestaande ingestie + ACK flow
            var result = await ingestion.IngestAsync(
                fhirJson,
                parsed.OrganisationId,
                ct);

            if (!result.Success)
            {
                return Results.BadRequest(result.Error ?? "HL7 message could not be processed.");
            }

            return Results.Ok(new
            {
                message = "HL7 message processed",
                appointmentId = result.AppointmentId,
                scheduled = result.Scheduled
            });
        });

        return app;
    }
}