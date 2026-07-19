using AnimeRecommendations.Domain;
using Koan.Data.Core;
using Microsoft.Extensions.Logging;

namespace AnimeRecommendations.Initialization;

/// <summary>The small, intentionally varied catalog that makes a clean checkout useful immediately.</summary>
internal static class StarterCatalog
{
    internal const string DemoViewerId = "demo";

    public static async Task Ensure(ILogger logger, CancellationToken ct)
    {
        if ((await Anime.FirstPage(1, ct)).Count == 0)
        {
            var catalog = Items();
            logger.LogInformation("Curating {Count} starter anime for local discovery.", catalog.Length);
            await catalog.Save(ct);
        }

        if (await Viewer.Get(DemoViewerId, ct) is null)
        {
            await new Viewer
            {
                Id = DemoViewerId,
                Name = "Mika"
            }.Save(ct);
        }

        await EnsureRating("cowboy-bebop", 5, ct);
        await EnsureRating("frieren", 5, ct);
        await EnsureRating("spy-x-family", 4, ct);
    }

    private static async Task EnsureRating(string animeId, int rating, CancellationToken ct)
    {
        var id = LibraryEntry.Key(DemoViewerId, animeId);
        if (await LibraryEntry.Get(id, ct) is null)
            await LibraryEntry.Record(DemoViewerId, animeId, rating).Save(ct);
    }

    private static Anime[] Items() =>
    [
        Item("cowboy-bebop", "Cowboy Bebop", 1998, 26, 8.8, "#d46b3c",
            "Bounty hunters cross the solar system while old regrets keep catching up with them.",
            ["Sci-Fi", "Action", "Drama"], ["Space", "Found Family", "Noir"]),
        Item("frieren", "Frieren: Beyond Journey's End", 2023, 28, 9.1, "#76a7c8",
            "An elven mage retraces a heroic journey and learns how brief human lives can still reshape her own.",
            ["Fantasy", "Adventure", "Drama"], ["Memory", "Friendship", "Quiet Journey"]),
        Item("spy-x-family", "Spy × Family", 2022, 25, 8.5, "#bb6f78",
            "A spy, an assassin, and a telepath pretend to be a family and slowly become one.",
            ["Comedy", "Action", "Slice of Life"], ["Found Family", "Secret Identity", "Warmhearted"]),
        Item("fullmetal-alchemist-brotherhood", "Fullmetal Alchemist: Brotherhood", 2009, 64, 9.1, "#b55a3c",
            "Two brothers pursue a cure for an alchemical catastrophe and uncover a conspiracy larger than their country.",
            ["Fantasy", "Action", "Drama"], ["Brotherhood", "Sacrifice", "Conspiracy"]),
        Item("steins-gate", "Steins;Gate", 2011, 24, 9.0, "#6e805a",
            "A scrappy laboratory discovers time travel and must face the human cost of changing the past.",
            ["Sci-Fi", "Thriller", "Drama"], ["Time Travel", "Friendship", "Conspiracy"]),
        Item("violet-evergarden", "Violet Evergarden", 2018, 13, 8.7, "#719dc1",
            "A former child soldier writes letters for others while learning the meaning of love and grief.",
            ["Drama", "Fantasy", "Slice of Life"], ["Healing", "Letters", "Memory"]),
        Item("mob-psycho-100", "Mob Psycho 100", 2016, 12, 8.6, "#cf7c45",
            "A gentle teenager with overwhelming psychic power works on growing as a person instead of becoming stronger.",
            ["Action", "Comedy", "Supernatural"], ["Coming of Age", "Mentorship", "Self-Acceptance"]),
        Item("a-place-further-than-the-universe", "A Place Further Than the Universe", 2018, 13, 8.5, "#66a9ba",
            "Four girls travel toward Antarctica and discover courage, friendship, and a way through loss.",
            ["Adventure", "Comedy", "Drama"], ["Friendship", "Travel", "Coming of Age"]),
        Item("odd-taxi", "Odd Taxi", 2021, 13, 8.5, "#628068",
            "A quiet taxi driver's ordinary conversations knot together into a sharp urban mystery.",
            ["Mystery", "Drama", "Comedy"], ["Ensemble", "Crime", "Urban Life"]),
        Item("mushishi", "Mushishi", 2005, 26, 8.7, "#728b72",
            "A wandering healer studies ethereal life forms and the delicate troubles they cause in remote communities.",
            ["Fantasy", "Mystery", "Slice of Life"], ["Nature", "Folklore", "Quiet Journey"]),
        Item("haikyuu", "Haikyu!!", 2014, 25, 8.7, "#e1833d",
            "An undersized volleyball player turns rivalry and teamwork into a path toward the national stage.",
            ["Sports", "Comedy", "Drama"], ["Teamwork", "Rivalry", "Growth"]),
        Item("kaguya-sama", "Kaguya-sama: Love Is War", 2019, 12, 8.5, "#b64d65",
            "Two brilliant student leaders turn their mutual crush into an escalating contest of romantic strategy.",
            ["Comedy", "Romance", "Slice of Life"], ["School", "Mind Games", "Friendship"]),
        Item("samurai-champloo", "Samurai Champloo", 2004, 26, 8.5, "#bc6748",
            "A determined traveler and two rival swordsmen cross an anachronistic Japan in search of a mysterious samurai.",
            ["Action", "Adventure", "Drama"], ["Road Trip", "Samurai", "Found Family"]),
        Item("made-in-abyss", "Made in Abyss", 2017, 13, 8.6, "#7d8c67",
            "A young explorer descends into a beautiful, lethal abyss to uncover the fate of her mother.",
            ["Fantasy", "Adventure", "Mystery"], ["Exploration", "Survival", "Dark Wonder"]),
        Item("your-lie-in-april", "Your Lie in April", 2014, 22, 8.6, "#d891a7",
            "A withdrawn pianist meets a fearless violinist who pulls music and feeling back into his life.",
            ["Drama", "Romance", "Music"], ["Healing", "Grief", "Coming of Age"]),
        Item("apothecary-diaries", "The Apothecary Diaries", 2023, 24, 8.8, "#7aa36f",
            "A keen-eyed apothecary solves medical mysteries and political puzzles inside an imperial palace.",
            ["Mystery", "Drama", "Historical"], ["Medicine", "Court Intrigue", "Clever Heroine"]),
        Item("delicious-in-dungeon", "Delicious in Dungeon", 2024, 24, 8.6, "#85a45e",
            "Adventurers cook monsters while racing through a dungeon to rescue a teammate.",
            ["Fantasy", "Adventure", "Comedy"], ["Food", "Teamwork", "Worldbuilding"]),
        Item("cyberpunk-edgerunners", "Cyberpunk: Edgerunners", 2022, 10, 8.6, "#dc4b73",
            "A reckless street kid joins a mercenary crew in a neon city that consumes ambition.",
            ["Sci-Fi", "Action", "Drama"], ["Dystopia", "Found Family", "Tragedy"]),
        Item("bocchi-the-rock", "Bocchi the Rock!", 2022, 12, 8.7, "#de83a1",
            "A painfully shy guitarist joins a band and finds connection one awkward performance at a time.",
            ["Comedy", "Music", "Slice of Life"], ["Friendship", "Social Anxiety", "Growth"]),
        Item("trigun-stampede", "Trigun Stampede", 2023, 12, 8.0, "#d25c42",
            "A pacifist gunslinger crosses a desert world while a catastrophic family conflict closes in.",
            ["Sci-Fi", "Action", "Drama"], ["Desert", "Pacifism", "Brotherhood"]),
        Item("eizouken", "Keep Your Hands Off Eizouken!", 2020, 12, 8.3, "#4e9a91",
            "Three students combine imagination, craft, and ruthless planning to make animation together.",
            ["Comedy", "Adventure", "Slice of Life"], ["Creativity", "Friendship", "Filmmaking"]),
        Item("pluto", "PLUTO", 2023, 8, 8.6, "#536b78",
            "A robot detective investigates a string of murders that threatens the fragile peace between humans and machines.",
            ["Sci-Fi", "Mystery", "Drama"], ["Artificial Intelligence", "War", "Humanity"]),
        Item("vinland-saga", "Vinland Saga", 2019, 24, 8.8, "#8a7154",
            "A warrior raised by vengeance searches for purpose beyond violence in the age of Vikings.",
            ["Action", "Adventure", "Drama"], ["Revenge", "War", "Redemption"]),
        Item("natsumes-book-of-friends", "Natsume's Book of Friends", 2008, 13, 8.5, "#8a9b73",
            "A lonely boy who sees spirits returns their names and slowly builds a home among humans and yokai.",
            ["Fantasy", "Slice of Life", "Supernatural"], ["Healing", "Folklore", "Belonging"])
    ];

    private static Anime Item(
        string id,
        string title,
        int year,
        int episodes,
        double score,
        string accent,
        string synopsis,
        string[] genres,
        string[] themes) => new()
        {
            Id = id,
            Title = title,
            Year = year,
            Episodes = episodes,
            CommunityScore = score,
            Accent = accent,
            Synopsis = synopsis,
            Genres = genres,
            Themes = themes
        };
}
