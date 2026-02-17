using MetaAnime.Application.Interfaces;
using MetaAnime.Application.Services;
using MetaAnime.Core.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaAnime.Controllers
{
    [Authorize]
    [Route("api/animes")]
    public class AnimeController(IAnimeMediatorService animeMediatorService, IAnimeMatchingService animeMatchingService)
        : Controller
    {
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await animeMatchingService.MatchAsync();
            //var result = await animeMediatorService.SyncAllAsync(forceRefreshKometaMapping: true);
            var result = await animeMediatorService.GetAllAsync();
            sw.Stop();
            return Ok(new {
                success = result.IsSuccess,
                message = result.IsSuccess ? "Success" : result.Error.Message,
                elapsedTime = sw.ElapsedMilliseconds,
                count = result.IsSuccess ? (int?)result.Value.Count() : null,
                separator= "=============================================",
                results = result.IsSuccess ? result.Value : null,
            });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            return Ok();
        }
    }
}
