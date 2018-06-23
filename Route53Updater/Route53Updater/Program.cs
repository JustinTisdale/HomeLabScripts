using Amazon;
using Amazon.Route53;
using Amazon.Route53.Model;
using Amazon.Runtime.CredentialManagement;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Route53Updater
{

    class Program
    {
        private const string _configFileName = "config.json";

        static Settings Settings { get; set; }

        static DateTime startTime;

        private static Dictionary<string, CommandOption> Options = new Dictionary<string, CommandOption>();

        static async Task Main(string[] args)
        {
            //TODO: Option to disable logging
            //TODO: Options to pass in all config variables
            //TODO: Option to provide a path to the config file
            //TODO: Option to change the log file name

            CommandLineApplication app = new CommandLineApplication();

            app.HelpOption("-?|-h|--help");
            app.Description = "Updates one or more AWS Route53 A-records with this machine's current external IP address.";
            app.ExtendedHelpText = "Usage: dotnet Route53Updater.dll -c config.json";

            Options.Add("quiet", app.Option("-q|--quiet", "Do not emit log files or verbose console messages.", CommandOptionType.NoValue));
            Options.Add("configPath", app.Option("-c|--config", "Path to the config json file", CommandOptionType.SingleValue));
            Options.Add("aws_access_key_id", app.Option("--aws-key-id", "AWS access key ID", CommandOptionType.SingleValue));
            Options.Add("aws_secret_access_key", app.Option("--aws-key-secret", "AWS access key secret", CommandOptionType.SingleValue));
            Options.Add("aws_region", app.Option("--aws-region", "What AWS region to use", CommandOptionType.SingleValue));
            Options.Add("aws_hosted_zone", app.Option("--aws-hosted-zone", "What Route 53 Hosted Zone contains the record to be updated.", CommandOptionType.SingleValue));
            Options.Add("subdomains", app.Option("-s|--subdomain", "Add a subdomain to be updated.", CommandOptionType.MultipleValue));


            // Pull in the config values
            IConfigurationBuilder builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(_configFileName, true);

            Settings = new Settings(builder.Build());

            app.OnExecute(() =>
            {
                SetupLogging();
                return DoUpdate();
            });

            app.Execute(args);
        }

        private static void SetupLogging()
        {
            if(!Settings.LoggingEnabled)
            {
                return;
            }

            if (!Directory.Exists("Route53Updater-logs"))
            {
                Directory.CreateDirectory("Route53Updater-logs");
            }

            Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.File("Route53Updater-logs/log.txt", rollingInterval: RollingInterval.Month)
                    .WriteTo.Console()
                    .CreateLogger();
        }

        private async static Task DoUpdate()
        {
            startTime = DateTime.Now;

            Log.Information("[Runtime = {StartTime}] Starting Update.", startTime);

            // Get current IP address
            Log.Information("[Runtime = {StartTime}] Getting external IP address.", startTime);
            string externalIPAddress = await GetExternalIPAddress();

            if (string.IsNullOrWhiteSpace(externalIPAddress))
            {
                Log.Warning("[Runtime = {StartTime}] Could not get external IP Address.", startTime);
            }
            else
            {
                Log.Information("[Runtime = {StartTime}] Got external IP address \"{ExternalIP}\"", startTime, externalIPAddress);

                string hostedZoneId = await GetHostedZoneIdByName(Settings.HostedZone);
                Log.Information("[Runtime = {StartTime}] Found Hosted Zone with Id \"{HostedZoneId}\"", startTime, hostedZoneId);

                foreach(string subdomain in Settings.Subdomains)
                {
                    string fqdn = subdomain + "." + Settings.HostedZone;
                    bool updateRequestAccepted = await UpdateIpAddressForSubdomain(hostedZoneId, fqdn, externalIPAddress);
                }
            }

            Log.Information("[Runtime = {StartTime}] Finished.", startTime);
            Console.WriteLine("Program finished. Exiting now.");
        }

        private async static Task<string> GetExternalIPAddress()
        {
            try
            {
                using (HttpClient httpClient = new HttpClient())
                {
                    string address = "";

                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://ipv4.icanhazip.com");

                    HttpResponseMessage response = await httpClient.SendAsync(request);

                    if (response != null && response.IsSuccessStatusCode)
                    {
                        address = await response.Content.ReadAsStringAsync();
                    }
                    else
                    {
                    }

                    return address?.Trim();
                }
            }
            catch(Exception exception)
            {
                Log.Error(exception, "[Runtime = {StartTime}] Exception thrown when trying to get external IP address.", startTime);
                return null;
            }
        }

        private async static Task<string> GetHostedZoneIdByName(string hostedZoneName)
        {
            AmazonRoute53Config config = new AmazonRoute53Config()
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(Settings.AWSRegion) // TODO: inject
            };

            using (AmazonRoute53Client route53Client = new AmazonRoute53Client(Settings.AWSAccessKeyId, Settings.AWSAccessKeySecret, config))
            {
                ListHostedZonesByNameResponse zones = await route53Client.ListHostedZonesByNameAsync(new ListHostedZonesByNameRequest() { DNSName = hostedZoneName });
                HostedZone matchingZone = zones?.HostedZones.FirstOrDefault(zone => zone.Name == hostedZoneName);
                return matchingZone?.Id;
            }
        }

        private async static Task<bool> UpdateIpAddressForSubdomain(string hostedZoneId, string fqdn, string newIpAddress)
        {
            AmazonRoute53Config config = new AmazonRoute53Config()
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(Settings.AWSRegion) // TODO: inject
            };

            using (AmazonRoute53Client route53Client = new AmazonRoute53Client(Settings.AWSAccessKeyId, Settings.AWSAccessKeySecret, config))
            {
                ListResourceRecordSetsResponse records = await route53Client.ListResourceRecordSetsAsync(new ListResourceRecordSetsRequest(hostedZoneId));

                ResourceRecordSet matchingRecordSet = records?.ResourceRecordSets.FirstOrDefault(prop => prop.Name == fqdn && prop.Type == RRType.A);

                if (matchingRecordSet != null && matchingRecordSet.ResourceRecords.FirstOrDefault() != null)
                {
                    if (matchingRecordSet.ResourceRecords.FirstOrDefault().Value != newIpAddress)
                    {
                        matchingRecordSet.ResourceRecords.FirstOrDefault().Value = newIpAddress;
                        ChangeBatch change = new ChangeBatch();
                        change.Changes.Add(new Change(ChangeAction.UPSERT, matchingRecordSet));

                        ChangeResourceRecordSetsResponse changeRequest = await route53Client.ChangeResourceRecordSetsAsync(new ChangeResourceRecordSetsRequest(hostedZoneId, change));
                        Log.Information("[Runtime = {StartTime}] Change request submitted to change subdomain {Subdomain} IP address to {IPAddress}.", startTime, fqdn, newIpAddress);

                        return changeRequest.HttpStatusCode == System.Net.HttpStatusCode.OK;
                    }
                    else
                    {
                        Log.Information("[Runtime = {StartTime}] Subdomain {Subdomain} found, but the IP address was already {IPAddress}.", startTime, fqdn, newIpAddress);
                    }
                }
                else
                {
                    // New subdomain
                    Log.Information("[Runtime = {StartTime}] Subdomain {Subdomain} record not found.", startTime, fqdn);
                }

                return false;
            }
        }
    }
}
