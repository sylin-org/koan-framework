# Troubleshooting

- Docs build: use the docs build task; treat warnings as errors
- Database adapters: check connection strings and DDL policy (see adapter guides)
- Testcontainers on Windows: Ryuk disabled; set DOCKER_HOST to npipe when needed
- Web Auth TestProvider in containers: callback URL resolves to localhost:8080 → see web-auth-testprovider-container-callback.md
- Web Auth TestProvider login: double /.testoauth prefix on authorize → see web-auth-testprovider-double-prefix-authorize.md
