namespace ProjektLabor.Services;

/// <summary>
/// Közös lapozási korlát: a nagyon nagy oldalszám esetén a (page - 1) * pageSize szorzat túlcsordulna
/// (negatív OFFSET → SQL Server-hiba, 500). Ennyi oldalon túl úgysem lehet adat, ezért levágjuk.
/// </summary>
public static class Pagination
{
    public const int MaxPage = 100_000;

    public static int NormalizePage(int? page) => Math.Clamp(page.GetValueOrDefault(1), 1, MaxPage);
}
