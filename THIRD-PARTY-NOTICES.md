# Third-party notices

Brainy is licensed under the [GNU Affero General Public License v3.0 only](LICENSE). It uses the third-party components listed below, each under its own license. The licenses come from each package's NuGet metadata or upstream repository; see each project for the full license text and copyright notices.

The .NET runtime and ASP.NET Core are © .NET Foundation and Contributors, licensed under the MIT License.

## Runtime dependencies (NuGet)

These packages ship with the application.

| Package | Version | License |
|---|---|---|
| [Azure.AI.OpenAI](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.OpenAI_2.1.0/sdk/openai/Azure.AI.OpenAI/README.md) | 2.1.0 | MIT |
| [Bogus](https://github.com/bchavez/Bogus) | 35.6.5 | MIT |
| [Heron.MudCalendar](https://danheron.github.io/Heron.MudCalendar) | 3.4.0 | MIT |
| [MailKit](http://www.mimekit.net/) | 4.17.0 | MIT |
| [Markdig](https://xoofx.github.io/markdig) | 1.2.0 | BSD-2-Clause |
| [Microsoft.AspNetCore.Identity.EntityFrameworkCore](https://asp.net/) | 10.0.11 | MIT |
| [Microsoft.EntityFrameworkCore](https://docs.microsoft.com/ef/core/) | 10.0.11 | MIT |
| [Microsoft.EntityFrameworkCore.Design](https://docs.microsoft.com/ef/core/) | 10.0.11 | MIT |
| [Microsoft.EntityFrameworkCore.SqlServer](https://docs.microsoft.com/ef/core/) | 10.0.11 | MIT |
| [Microsoft.Extensions.AI](https://dot.net/) | 10.7.0 | MIT |
| [Microsoft.Extensions.AI.OpenAI](https://dot.net/) | 10.7.0 | MIT |
| [Microsoft.Extensions.Caching.Memory](https://dot.net/) | 10.0.11 | MIT |
| [Microsoft.Extensions.Options.ConfigurationExtensions](https://dot.net/) | 10.0.11 | MIT |
| [ModelContextProtocol.AspNetCore](https://csharp.sdk.modelcontextprotocol.io/) | 2.2.0 | Apache-2.0 |
| [MudBlazor](https://mudblazor.com/) | 8.4.0 | MIT |
| [OpenTelemetry.Exporter.OpenTelemetryProtocol](https://opentelemetry.io/) | 1.18.0 | Apache-2.0 |
| [OpenTelemetry.Extensions.Hosting](https://opentelemetry.io/) | 1.18.0 | Apache-2.0 |
| [OpenTelemetry.Instrumentation.AspNetCore](https://opentelemetry.io/) | 1.18.0 | Apache-2.0 |
| [OpenTelemetry.Instrumentation.EntityFrameworkCore](https://opentelemetry.io/) | 1.18.0-beta.1 | Apache-2.0 |
| [OpenTelemetry.Instrumentation.Http](https://opentelemetry.io/) | 1.18.0 | Apache-2.0 |
| [Stripe.net](https://github.com/stripe/stripe-dotnet) | 52.4.2 | Apache-2.0 |
| [System.Security.Cryptography.Xml](https://dot.net/) | 10.0.11 | MIT |
| [WebPush](https://github.com/web-push-libs/web-push-csharp/) | 1.0.13 | MPL-2.0 |

## Development and test dependencies (NuGet and .NET tools)

These packages are used only for building and testing, and don't ship with the application.

| Package | Version | License |
|---|---|---|
| [AwesomeAssertions](https://github.com/AwesomeAssertions/AwesomeAssertions) | 9.5.0 | Apache-2.0 |
| [coverlet.collector](https://github.com/coverlet-coverage/coverlet) | 6.0.4 | MIT |
| [dotnet-ef](https://docs.microsoft.com/ef/core/) | 10.0.11 | MIT |
| [Microsoft.AspNetCore.Mvc.Testing](https://asp.net/) | 10.0.11 | MIT |
| [Microsoft.EntityFrameworkCore.InMemory](https://docs.microsoft.com/ef/core/) | 10.0.11 | MIT |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | 17.14.1 | MIT |
| [Microsoft.Playwright](https://github.com/microsoft/playwright-dotnet) | 1.56.0 | MIT |
| [ModelContextProtocol](https://csharp.sdk.modelcontextprotocol.io/) | 2.2.0 | Apache-2.0 |
| [xunit](https://github.com/xunit/xunit) | 2.9.3 | Apache-2.0 |
| [xunit.runner.visualstudio](https://github.com/xunit/visualstudio.xunit) | 2.8.2 | Apache-2.0 |

## License compatibility

All the licenses above are permissive (MIT, Apache-2.0, BSD-2-Clause) except **WebPush**, which uses the **Mozilla Public License 2.0**. MPL-2.0 is a file-level copyleft license that is compatible with the AGPL-3.0 (MPL-2.0 section 3.3). Brainy uses the package unmodified. If you modify WebPush's own source files, you must publish those changes under MPL-2.0.
