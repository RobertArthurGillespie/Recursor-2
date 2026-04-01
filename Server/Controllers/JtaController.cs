using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.AI.OpenAI;
//using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;

namespace NCATAIBlazorFrontendTest.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class JtaController : ControllerBase
    {
        [HttpGet("Test")]
        public IActionResult Test()
        {
            return Content("it's working!");
        }
    }
}
