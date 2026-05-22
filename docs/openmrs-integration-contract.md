# OpenMRS integration contract

## Short version

- OpenMRS version: **2.7+**
- Transport: **webhook**
- Fallback: **polling only if needed**
- Message type: **FHIR R4 Appointment**

## Expected resources

- `Appointment`
- `Patient` when the phone number is inside the message
- `Practitioner` and `Location` if OpenMRS sends those references

## What the OpenMRS admin needs

- API URL of the communication module
- `X-OpenMRS-Instance-Id`
- `X-OpenMRS-Access-Key`

## Behaviour

- The API checks the instance key first.
- If the message is valid, the API returns a simple ACK.
- If the message is invalid, the API returns an error response.
