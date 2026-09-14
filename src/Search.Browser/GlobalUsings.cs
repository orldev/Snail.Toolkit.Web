global using PageOutcome = Snail.Toolkit.Web.Outcome<
    Snail.Toolkit.Web.Scraping.PageContent,
    Snail.Toolkit.Web.Scraping.PageFailure>;
global using SearchAnswer = Snail.Toolkit.Web.Outcome<
    System.Collections.Generic.IReadOnlyList<Snail.Toolkit.Web.Search.SearchResult>,
    Snail.Toolkit.Web.Search.SearchFailure>;
