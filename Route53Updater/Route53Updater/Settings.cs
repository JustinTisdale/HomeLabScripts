using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Route53Updater
{
    public class Settings
    {
        public Settings()
        {

        }

        public Settings(IConfiguration configuration)
        {
            if (configuration != null)
            {
                AWSAccessKeyId = configuration["aws_access_key_id"];
                AWSAccessKeySecret = configuration["aws_secret_access_key"];
                AWSRegion = configuration["aws_region"];
                HostedZone = configuration["aws_hosted_zone"];
                Subdomains = configuration.GetSection("subdomains")?.GetChildren().Select(item => item.Value).ToList();
            }

            StartTime = DateTime.Now;
        }

        public bool LoggingEnabled { get; set; } = true;

        public string AWSAccessKeyId { get; set; }

        public string AWSAccessKeySecret { get; set; }

        public string AWSRegion { get; set; }

        public string HostedZone { get; set; }

        public List<string> Subdomains { get; set; } = new List<string>();

        public DateTime StartTime { get; set; }
    }
}
