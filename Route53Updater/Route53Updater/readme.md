# Route53Updater

## Purpose

Updates a list of subdomains on AWS Route53 with the machine's current external IP address.

## Configuration (File-based)

Rename config-example.json to config.json and update the config settings.

## Configuration (Argument-based)

TODO

## Usage (Windows)

    dotnet publish -c Release -o "SomeFolderPath"
    cd "SomeFolderPath"
    dotnet Route53Updater.dll -c config.json

## Dependencies

* .Net Core 2.1
* AWS SDK
* McMaster.Extensions.CommandLineUtils
* RestSharp
* Serilog

## TODO

* Configuration documentation
* Batch updates into one request
* Create records for subdomains that don't already exist

