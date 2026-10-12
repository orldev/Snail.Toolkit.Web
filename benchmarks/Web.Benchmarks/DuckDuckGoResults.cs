using System.Text;

namespace Snail.Toolkit.Web.Benchmarks;

/// <summary>A DuckDuckGo results page in the shape its HTML endpoint serves: thirty results and the next-page form.</summary>
public static class DuckDuckGoResults
{
    /// <summary>Gets the query the page answers.</summary>
    public const string Query = "grain prices";

    /// <summary>Builds the page.</summary>
    /// <returns>The markup.</returns>
    public static string Build()
    {
        var html = new StringBuilder(64 * 1024);

        html.Append("<!DOCTYPE html><html><head><title>grain prices at DuckDuckGo</title><link rel=\"stylesheet\" href=\"/dist/s.css\">");
        html.Append("<script>var x=1;</script></head><body class=\"body--html\"><div class=\"header\"><form action=\"/html/\" method=\"post\">");
        html.Append("<input type=\"text\" name=\"q\" value=\"grain prices\"><input type=\"submit\" value=\"S\"></form></div><div id=\"links\" class=\"results\">");

        for (var index = 0; index < 30; index++)
        {
            html.Append("<div class=\"result results_links results_links_deep web-result \"><div class=\"links_main links_deep result__body\">");
            html.Append("<h2 class=\"result__title\"><a rel=\"nofollow\" class=\"result__a\" href=\"//duckduckgo.com/l/?uddg=https%3A%2F%2Fsite");
            html.Append(index).Append(".example%2Fgrain%2Fprices%3Fid%3D").Append(index);
            html.Append("&amp;rut=a1b2c3d4e5f6\">Grain prices, report ").Append(index).Append(" &amp; more</a></h2>");
            html.Append("<div class=\"result__extras\"><div class=\"result__extras__url\"><span class=\"result__icon\">");
            html.Append("<img class=\"result__icon__img\" width=\"16\" height=\"16\" alt=\"\" src=\"//external-content.duckduckgo.com/ip3/site.ico\" /></span>");
            html.Append("<a class=\"result__url\" href=\"//duckduckgo.com/l/?uddg=x\">site").Append(index).Append(".example/grain/prices</a></div></div>");
            html.Append("<a class=\"result__snippet\" href=\"//duckduckgo.com/l/?uddg=x\">Wheat, barley and maize <b>prices</b> at the exchange, ");
            html.Append("with the week's change and the harvest outlook.</a><div class=\"clear\"></div></div></div>");
        }

        html.Append("<div class=\"nav-link\"><form action=\"/html/\" method=\"post\"><input type=\"submit\" class=\"btn btn--alt\" value=\"Next\" />");
        html.Append("<input type=\"hidden\" name=\"q\" value=\"grain prices\" /><input type=\"hidden\" name=\"s\" value=\"30\" />");
        html.Append("<input type=\"hidden\" name=\"dc\" value=\"31\" /><input type=\"hidden\" name=\"vqd\" value=\"4-123\" /></form></div>");
        html.Append("</div></body></html>");

        return html.ToString();
    }
}
