namespace S7.TechDocs.Infrastructure;

public static class Constants
{
    public static class Roles
    {
        public const string Reader = "Reader";
        public const string Author = "Author";
        public const string Moderator = "Moderator";
        public const string Admin = "Admin";
    }

    public static class DocumentStatus
    {
        public const string Draft = "Draft";
        public const string Review = "Review";
        public const string Published = "Published";
        public const string Archived = "Archived";
    }

    public static class Collections
    {
        public const string GettingStarted = "getting-started";
        public const string Guides = "guides";
        public const string ApiReference = "api-reference";
        public const string Faq = "faq";
        public const string Troubleshooting = "troubleshooting";
    }

    public static class ApiEndpoints
    {
        public const string Documents = "/api/documents";
        public const string Collections = "/api/collections";
        public const string Users = "/api/users";
        public const string Search = "/api/search";
        public const string AI = "/api/ai";
    public const string Engagement = "/api/engagement";
    }
}
