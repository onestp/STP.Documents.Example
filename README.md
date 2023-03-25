# STP.Documents.Examples

This repository contains example code to show how the STP.Documents APIs can be used to integrate with LEXolution.DMS.


## OnPremise.Server

This folder contains example code for how to communicate with the DMS server from another server. It uses impersonation to perfom actions on the users behalf. Impersonating users is something only a secure server should be able to do.


## OnPremise.Client (coming soon)

This folder contains example code for how to communicate with the DMS server from a client application. It uses the LCAS as a client proxy.


## Cloud.Channel.OnPremise (coming soon)

This folder contains example code for how to communicate with the DMS over the cloud. It uses the STP.Documents.Channel.OnPremise service (DMS Mobile DESK uses it too) to establish and end-to-end encrypted connection to the DMS server.


## Cloud.Store

This folder contains example code for how to store, manage and retrieve documents in the cloud.
