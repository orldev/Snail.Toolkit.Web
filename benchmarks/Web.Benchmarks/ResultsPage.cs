using System.Text;

namespace Snail.Toolkit.Web.Benchmarks;

/// <summary>A search engine's results page of the size the audit read: about 250 KB, twenty results among the furniture.</summary>
/// <remarks>
/// Marked up the way Brave marks its results — <c>data-type="web"</c> blocks with a titled heading and a snippet — so
/// the engine's own parser finds them, and surrounded by what such a page also carries: scripts, styles, inline
/// icons, filters and related searches.
/// </remarks>
public static class ResultsPage
{
    /// <summary>Gets the query the page answers.</summary>
    public const string Query = "grain prices";

    /// <summary>Builds the page.</summary>
    /// <returns>The markup.</returns>
    public static string Build()
    {
        var html = new StringBuilder(260 * 1024);

        html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>grain prices - Search</title>");

        for (var i = 0; i < 12; i++)
        {
            html.Append("<script>window.__data=window.__data||[];window.__data.push({route:'/search',chunk:");
            html.Append(i).Append(",flags:[1,2,3,4,5,6,7,8]});</script>");
        }

        html.Append("<style>body{margin:0}.snippet{padding:8px}.search-snippet-title{font-weight:600}.content{color:#444}</style></head><body>");
        html.Append("<header><form action=\"/search\"><input name=\"q\" value=\"grain prices\"></form><nav>");

        for (var i = 0; i < 40; i++)
        {
            html.Append("<a href=\"/search?q=grain+prices&amp;tf=").Append(i);
            html.Append("\"><svg viewBox=\"0 0 16 16\"><path d=\"M1 1h14v14H1z\"/></svg>Filter ").Append(i).Append("</a>");
        }

        html.Append("</nav></header><main>");

        for (var result = 0; result < 20; result++)
        {
            html.Append("<div class=\"snippet svelte-jmfu5f\" data-type=\"web\" data-pos=\"").Append(result).Append("\">");
            html.Append("<a href=\"https://market").Append(result).Append(".example.com/grain/prices/");
            html.Append(result).Append("?utm_source=search\">");
            html.Append("<div class=\"search-snippet-title svelte-14r20fy\" title=\"Grain prices today, report ").Append(result);
            html.Append("\">Grain prices today, report ").Append(result).Append("</div></a>");
            html.Append("<div class=\"generic-snippet\"><div class=\"content\">");

            for (var sentence = 0; sentence < 6; sentence++)
                html.Append("Wheat, barley and maize prices at the exchange, with the week's change and the harvest outlook. ");

            html.Append("</div></div>");

            for (var icon = 0; icon < 30; icon++)
                html.Append("<span class=\"icon\"><svg viewBox=\"0 0 24 24\"><path d=\"M12 2l3 7h7l-5.5 4.5L18 21l-6-4-6 4 1.5-7.5L2 9h7z\"/></svg></span>");

            html.Append("</div>");
        }

        html.Append("<aside><h2>Related searches</h2><ul>");

        for (var i = 0; i < 60; i++)
        {
            html.Append("<li><a href=\"/search?q=grain+prices+").Append(i);
            html.Append("\">grain prices ").Append(i).Append("</a></li>");
        }

        html.Append("</ul></aside></main><footer><a href=\"https://accounts.example.com/\">Sign in</a></footer></body></html>");

        return html.ToString();
    }
}
