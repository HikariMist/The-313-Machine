using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using HikariForge.M313.Playtest;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<GameRoot>("#app");
await builder.Build().RunAsync();
