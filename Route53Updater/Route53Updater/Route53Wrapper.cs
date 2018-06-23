using Amazon;
using Amazon.Route53;
using Amazon.Route53.Model;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Route53Updater
{
    public class Route53Wrapper
    {
        public Settings Settings { get; set; }

        public AmazonRoute53Client GetAmazonRoute53Client()
        {
            AmazonRoute53Config config = new AmazonRoute53Config()
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(Settings.AWSRegion) // TODO: inject
            };

            AmazonRoute53Client route53Client = new AmazonRoute53Client(Settings.AWSAccessKeyId, Settings.AWSAccessKeySecret, config);

            return route53Client;
        }

        public async Task<string> GetHostedZoneIdByName(string hostedZoneName)
        {
            using (AmazonRoute53Client route53Client = GetAmazonRoute53Client())
            {
                ListHostedZonesByNameResponse zones = await route53Client.ListHostedZonesByNameAsync(new ListHostedZonesByNameRequest() { DNSName = hostedZoneName });
                HostedZone matchingZone = zones?.HostedZones.FirstOrDefault(zone => zone.Name == hostedZoneName);
                return matchingZone?.Id;
            }
        }

        public async Task<bool> UpdateIpAddressForSubdomain(string hostedZoneId, string fqdn, string newIpAddress)
        {
            using (AmazonRoute53Client route53Client = GetAmazonRoute53Client())
            {
                ListResourceRecordSetsResponse records = await route53Client.ListResourceRecordSetsAsync(new ListResourceRecordSetsRequest(hostedZoneId));

                // Look for an A record matching the FQDN that was passed in
                ResourceRecordSet matchingRecordSet = records?.ResourceRecordSets.FirstOrDefault(prop => prop.Name == fqdn && prop.Type == RRType.A);

                if (matchingRecordSet != null && matchingRecordSet.ResourceRecords.Any())
                {
                    if (matchingRecordSet.ResourceRecords.First().Value != newIpAddress)
                    {
                        matchingRecordSet.ResourceRecords.First().Value = newIpAddress;
                        ChangeBatch change = new ChangeBatch();
                        change.Changes.Add(new Change(ChangeAction.UPSERT, matchingRecordSet));

                        ChangeResourceRecordSetsResponse changeRequest = await route53Client.ChangeResourceRecordSetsAsync(new ChangeResourceRecordSetsRequest(hostedZoneId, change));
                        Log.Information("[Runtime = {StartTime}] Change request submitted to change subdomain {Subdomain} IP address to {IPAddress}.", Settings.StartTime, fqdn, newIpAddress);

                        return changeRequest.HttpStatusCode == System.Net.HttpStatusCode.OK;
                    }
                    else
                    {
                        Log.Information("[Runtime = {StartTime}] Subdomain {Subdomain} found, but the IP address was already {IPAddress}.", Settings.StartTime, fqdn, newIpAddress);
                    }
                }

                return false;
            }
        }
    }
}
