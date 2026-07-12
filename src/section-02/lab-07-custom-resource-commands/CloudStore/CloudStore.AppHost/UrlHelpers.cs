static class UrlHelpers
{
    extension<T>(IResourceBuilder<T> builder) where T : IResource
    {
        public IResourceBuilder<T> WithFriendlyUrls(string? displayText = null, string? endpointName = null, string? path = null)
        {
            return builder.WithUrls(c =>
            {
                List<string?> endpointNames = [endpointName, "https", "http"];
                var endpoint = endpointNames
                    .Where(n => n is not null)
                    .Select(n => c.GetEndpoint(n!))
                    .FirstOrDefault(e => e?.Exists ?? false);

                if (endpoint is null) return;

                displayText ??= builder.Resource.Name;
                foreach (var url in c.Urls)
                {
                    url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
                }

                c.Urls.Add(new()
                {
                    Endpoint = endpoint,
                    DisplayText = displayText,
                    DisplayLocation = UrlDisplayLocation.SummaryAndDetails,
                    Url = path ?? "/"
                });
                
            });
        }
    }
}