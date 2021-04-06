using Blazor.IndexedDB.Framework;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace IndexedDB_Demo.Services
{
    public interface IStorageService
    {
        Task CreateNewWeatherAsync();
        Task DeleteAsync(Guid id);
        Task<List<WeatherForecast>> GetWeathersAsync();
    }

    public class StorageService : IStorageService
    {
        private Lazy<Task<DbContext>> lazyDb;

        public StorageService(IIndexedDbFactory dbFactory)
        {
            lazyDb = new Lazy<Task<DbContext>>(async () => await dbFactory.Create<DbContext>());
        }

        public async Task CreateNewWeatherAsync()
        {
            await CheckCityAsync();
            using var db = await lazyDb.Value;
            {
                var randNum = new Random().Next(-35, 35);
                var randCity = new Random().Next(0, 6);
                var newW = new WeatherForecast()
                {
                    Date = DateTime.Now.AddDays(-new Random().Next(1, 50000)),
                    Summary = db.Cities.ToList()[randCity].Name,
                    TemperatureC = randNum
                };

                db.WeatherForecasts.Add(newW);
                await db.SaveChanges();
            }
        }

        private async Task CheckCityAsync()
        {
            using var db = await lazyDb.Value;
            {
                if (!db.Cities.Any())
                {
                    db.Cities.Add(new City { Name = "Madrid" });
                    db.Cities.Add(new City { Name = "Rome" });
                    db.Cities.Add(new City { Name = "Verona" });
                    db.Cities.Add(new City { Name = "Barcelona" });
                    db.Cities.Add(new City { Name = "Valencia" });
                    db.Cities.Add(new City { Name = "Napoli" });
                    await db.SaveChanges();
                }
            }
        }

        public async Task DeleteAsync(Guid id)
        {
            using var db = await lazyDb.Value;
            {
                var elementToRemove = db.WeatherForecasts.FirstOrDefault(t => t.Id == id);
                db.WeatherForecasts.Remove(elementToRemove);
                await db.SaveChanges();
            }
        }

        public async Task<List<WeatherForecast>> GetWeathersAsync()
        {
            using var db = await lazyDb.Value;
            {
                return db.WeatherForecasts.ToList();
            }
        }
    }

    public class DbContext : IndexedDb
    {
        public DbContext(IJSRuntime jSRuntime, string name, int version)
        : base(jSRuntime, name, version)
        {
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
