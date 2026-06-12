// The web layer now uses the structured SortSpec from Koan.Data.Abstractions.Sorting (DATA-0092).
// This file remains only to preserve the namespace for callers that imported Koan.Web.Hooks;
// the SortSpec type itself lives in Koan.Data.Abstractions.Sorting.

global using SortSpec = Koan.Data.Abstractions.Sorting.SortSpec;
