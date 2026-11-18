using System.Net;
using Quadra.Core.Qserv;

namespace Quadra.Server;

/// <summary>
/// The HTTP face of the game list server. The original was a CGI script at /cgi-bin/qserv.pl; that path
/// is kept so existing clients work unchanged, and the root path answers too.
/// </summary>
public static class QservHost
{
    public static readonly string[] Paths = ["/cgi-bin/qserv.pl", "/qserv", "/"];

    public static WebApplication Create(string[] args, QservServer? server = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Recordings travel inside the request, so allow somewhat more than the default form size.
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 2_000_000);

        server ??= new QservServer(new QservOptions
        {
            ScoresFile = builder.Configuration["Qserv:ScoresFile"],
            GameTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Qserv:GameTimeoutSeconds", 180)),
            ScoresToKeep = builder.Configuration.GetValue("Qserv:ScoresToKeep", 100),
        });
        builder.Services.AddSingleton(server);

        var app = builder.Build();

        // Behind a reverse proxy, set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true so the caller's address is used.
        foreach (var path in Paths)
            app.MapMethods(path, ["GET", "POST"], Handle);

        return app;
    }

    private static async Task<IResult> Handle(HttpContext http, QservServer server)
    {
        string data = "";
        if (http.Request.HasFormContentType)
            data = (await http.Request.ReadFormAsync())["data"].ToString();

        var remote = http.Connection.RemoteIpAddress ?? IPAddress.Loopback;
        if (remote.IsIPv4MappedToIPv6)
            remote = remote.MapToIPv4();

        try
        {
            return Results.Text(server.Handle(data, remote.ToString()), "text/plain");
        }
        catch (QservException e)
        {
            return Results.Text(e.Message + "\n", "text/plain", statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
