using System;
using System;
using Moq;
using Xunit;
using GradeSyncApi.Services.OneRoster;
using GradeSyncApi.Services.Storage;
using Microsoft.Extensions.Configuration;

namespace GradeSyncTest
{
	public class OneRosterService_Test
	{
		public OneRosterService GetService()
		{
            IConfiguration config = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile("secrets.test.json")
				.Build();
            return new OneRosterService(config);
		}

		[Fact]
		public async void Run()
		{
			var service = GetService();

			var connection = new OneRosterConnectionEntity();
			connection.OneRosterBaseUrl = "https://partnertest.infinitecampus.org/campus/oneroster/microsofttest/ims/oneroster/v1p1";
			connection.OAuth2TokenUrl = "https://partnertest.infinitecampus.org/campus/oauth2/token?appName=microsofttest";
			connection.ClientId = "meu!8aS3PSRji463";
			connection.ClientSecret = "TMiW*1Xl)ZmTqRuzBv-4MTiCUruJdR29";

			await service.InitApiConnection(connection);
			var categories = await service.GetActiveCategories();
			Console.WriteLine("test");
        }
	}
}

