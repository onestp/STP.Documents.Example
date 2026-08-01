# STP.Documents.Examples

This repository contains example code to show how the STP.Documents APIs can be used to integrate STPs documents-based capabilities.

## Prerequisites

* [.NET 10 SDK](https://dotnet.microsoft.com/download)
* Access to the `STP.Documents.*` NuGet packages. They are published to the [STP.TechnologyPartner](https://github.com/onestp/STP.TechnologyPartner) repository, whose feed is `https://nuget.pkg.github.com/onestp/index.json`. Technology partners are granted access to it; if you have no access yet, please approach your existing contacts at STP.
* See the "How to get started building" section of the [Universal API article](https://support.stp.one/hc/en-us/articles/30884480549789-Universal-API) in the STP support portal for the full setup walkthrough.

## Configuration

The client configuration, such as `ClientId` and credentials, is issued by STP once a technology partnership has been established. Please approach your existing contacts at STP to obtain it.

Put your values into an `appsettings.local.json` next to the `appsettings.json` of the example. It overrides the committed settings and is excluded from version control.

## OnPremise.Server

This folder contains example code for how to communicate with the DMS server from another server. It uses impersonation to perfom actions on the users behalf. Impersonating users is something only a secure server should be able to do.

## Universal

This folder contains example code for how to store, manage and retrieve documents in the cloud via the Universal API.

## MCP

This folder contains example code for how to connect with the STP.Documents MCP server. The example performs the OAuth authorization code flow in the browser, caches the resulting tokens on disk, lists the available tools and calls one of them.

See [MCP in the STP support portal](https://support.stp.one/hc/en-us/articles/30884494431389-MCP) for a description of the server and its tools.
