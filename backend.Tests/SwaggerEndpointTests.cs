using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjektLabor.Data;
using Xunit;

namespace backend.Tests
{
    public class SwaggerEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public SwaggerEndpointTests(WebApplicationFactory<Program> factory)
        {
            var dbName = "SwaggerTestDb_" + Guid.NewGuid();
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    services.AddDbContext<AppDbContext>(options =>
                    {
                        options.UseInMemoryDatabase(dbName);
                    });
                });
            });
        }

        [Fact]
        public async Task SwaggerJson_ReturnsOkAndContainsExistingEndpoints_InDevelopment()
        {
            // Arrange
            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/swagger/v1/swagger.json");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"openapi\": \"3.0.1\"", content);
            Assert.DoesNotContain("\"openapi\": \"3.0.4\"", content);
            Assert.Contains("\"ProjektLabor API\"", content);
            Assert.Contains("/api/health", content);
            Assert.Contains("/api/rooms", content);
            Assert.Contains("/api/rooms/{id}", content);
        }

        [Fact]
        public async Task SwaggerUI_IsAccessible_InDevelopment()
        {
            // Arrange
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = true
            });

            // Act
            var response = await client.GetAsync("/swagger");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("swagger", content, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Swagger_IsNotAvailable_InProduction()
        {
            // Arrange
            var prodFactory = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
            });
            var client = prodFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            // Act
            var swaggerJsonResponse = await client.GetAsync("/swagger/v1/swagger.json");
            var swaggerUiResponse = await client.GetAsync("/swagger");

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, swaggerJsonResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, swaggerUiResponse.StatusCode);
        }
    }
}
