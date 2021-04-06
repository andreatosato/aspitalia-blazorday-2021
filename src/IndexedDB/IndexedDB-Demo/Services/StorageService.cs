using Blazor.IndexedDB.Framework;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace IndexedDB_Demo.Services
{
    public class StorageService
    {
        private Lazy<Task<DbContext>> lazyDb;

        public StorageService(IIndexedDbFactory dbFactory)
        {
            lazyDb = new Lazy<Task<DbContext>>(async () => await dbFactory.Create<DbContext>());
        }

        public async Task Create()
        {
            using var db = await lazyDb.Value;
            {

            }
        }

        public async Task CreateNewWeatherAsync()
        {
            var randNum = new Random().Next(-35, 35);
            var randCity = new Random().Next(0, 6);
            var newW = new WeatherForecast()
            {
                Date = DateTime.Now.AddDays(-new Random().Next(1, 50000)),
                Summary = cities[randCity],
                TemperatureC = randNum
            };

            await _weatherCollection.InsertAsync(newW);
            forecasts = await _weatherCollection.Query().ToListAsync();
            StateHasChanged();
        }

        public async Task DeleteAsync(Guid id)
        {
            await _weatherCollection.DeleteAsync(new BsonValue(id));
            forecasts = await _weatherCollection.Query().ToListAsync();
            StateHasChanged();
        }
    }

    public class DbContext : IndexedDb
    {
        public DbContext(IJSRuntime jSRuntime, string name, int version)
        : base(jSRuntime, name, version)
        {
            Cities.Add(new City { Name = "Madrid" });
            Cities.Add(new City { Name = "Rome" });
            Cities.Add(new City { Name = "Verona" });
            Cities.Add(new City { Name = "Barcelona" });
            Cities.Add(new City { Name = "Valencia" });
            Cities.Add(new City { Name = "Napoli" });
        }

        public IndexedSet<WeatherForecast> WeatherForecasts { get; set; }
        public IndexedSet<City> Cities { get; set; }
    }

    public class WeatherForecast
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime Date { get; set; }

        public int TemperatureC { get; set; }

        public string Summary { get; set; }

        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    }

    public class City
    {
        [Key]
        public string Name { get; set; }
    }
}
