# Shelved Koan Aspire projection

This project preserves the experimental automatic AppHost contributor discovery source. It is outside the Koan V1
product, solution, package inventory, release graph, and public support promise.

The former in-application Docker lifecycle was removed when the project was shelved. It duplicated the authority of an
Aspire AppHost and depended on a connector-wide evaluator SPI with no other runtime consumer.

For current applications, author topology directly in an Aspire AppHost with standard resource integrations and
`WithReference`. Koan connectors consume the connection strings and service endpoints Aspire injects; no Koan Aspire
package is required.
