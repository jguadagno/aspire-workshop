# Aspire Workshop

This repository contains hands-on labs and source code for an Aspire workshop, [Aspire in Practice: Faster Onboarding, Better Observability, Smarter Distributed Development](https://www.josephguadagno.net/presentations/aspire-in-practice-faster-onboarding-better-observability-smarter-distributed-development), centered on CloudStore scenarios.

## Requirements

See [requirements.md](requirements.md) for the full software and environment checklist.

## Workshop Outline

The canonical workshop sequence is defined in [outline.md](outline.md):

1. Aspire Overview
2. Aspire Architecture
3. Add Aspire to an Existing Application
4. Deploying with Aspire
5. AI Coding Agents
6. Real-World Use Cases and Best Practices
7. Wrapping Up

## Labs

Labs are organized under [labs](labs) by section, each with a corresponding `src/` folder containing starter and completed code.

### Section 2 — Aspire Architecture

- [Lab 1: Your First Aspire Application](labs/section-02/lab-01-your-first-aspire-application.md)
- [Lab 2: Orchestrating Services with the App Host](labs/section-02/lab-02-orchestrating-services-with-the-app-host.md)
- [Lab 3: Adding Redis Caching](labs/section-02/lab-03-adding-redis-caching.md)
- [Lab 4: Adding a Microsoft SQL Server Database](labs/section-02/lab-04-adding-a-microsoft-sql-server-database.md)
- [Lab 5: Mastering the Aspire Dashboard](labs/section-02/lab-05-mastering-the-aspire-dashboard.md)
- [Lab 6: Custom Health Checks](labs/section-02/lab-06-custom-health-checks.md)
- [Lab 7: Custom Resource Commands](labs/section-02/lab-07-custom-resource-commands.md)

### Section 3 — Add Aspire to an Existing Application

- [Lab 1: Add Aspire](labs/section-03/lab-01-add-aspire.md)

### Section 4 — Deploying with Aspire

- [Lab 1: Deploying to Azure Container Apps](labs/section-04/lab-01-deploying-to-azure-container-apps.md)

## Repository Layout

```text
aspire-workshop/
├── README.md
├── outline.md
├── requirements.md
├── labs/
│   ├── section-02/
│   ├── section-03/
│   └── section-04/
└── src/
    ├── section-02/
    ├── section-03/
    └── section-04/
```

## References

- [Aspire Documentation](https://aspire.dev/)
- [Aspire CLI Overview](https://learn.microsoft.com/en-us/dotnet/aspire/aspire-cli/overview)
- [Aspire Deployment Guide](https://learn.microsoft.com/en-us/dotnet/aspire/deployment/overview)
- [Aspire Integrations Overview](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/integrations-overview)
- [Aspire GitHub Repository](https://github.com/microsoft/aspire)
- [Aspire Samples](https://github.com/dotnet/aspire-samples)
- Talk: [Aspire in Practice: Faster Onboarding, Better Observability, Smarter Distributed Development](https://www.josephguadagno.net/presentations/aspire-in-practice-faster-onboarding-better-observability-smarter-distributed-development)
