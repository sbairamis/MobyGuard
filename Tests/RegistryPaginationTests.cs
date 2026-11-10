using System.Net.Http;
using MobyGuard.Repos;

namespace Tests;

public class RegistryPaginationTests
{
    [Fact]
    public void ExtractNextLink_StandardDockerHeader_ExtractsRelativePath()
    {
        using var response = new HttpResponseMessage();
        response.Headers.Add("Link", "</v2/_catalog?last=infra%2Fnginx&n=100>; rel=\"next\"");

        var nextLink = RegistryRepository.ExtractNextLink(response.Headers);

        Assert.Equal("/v2/_catalog?last=infra%2Fnginx&n=100", nextLink);
    }

    [Fact]
    public void ExtractNextLink_AbsoluteUri_ExtractsPathAndQuery()
    {
        using var response = new HttpResponseMessage();
        response.Headers.Add("Link", "<http://registry:5000/v2/_catalog?last=team-b%2Fservice&n=50>; rel=\"next\"");

        var nextLink = RegistryRepository.ExtractNextLink(response.Headers);

        Assert.Equal("/v2/_catalog?last=team-b%2Fservice&n=50", nextLink);
    }

    [Fact]
    public void ExtractNextLink_MultipleLinks_FindsNextRelation()
    {
        using var response = new HttpResponseMessage();
        response.Headers.Add("Link", "</v2/_catalog?n=50>; rel=\"first\", </v2/_catalog?last=app-100&n=50>; rel=\"next\"");

        var nextLink = RegistryRepository.ExtractNextLink(response.Headers);

        Assert.Equal("/v2/_catalog?last=app-100&n=50", nextLink);
    }

    [Fact]
    public void ExtractNextLink_NoNextRelation_ReturnsNull()
    {
        using var response = new HttpResponseMessage();
        response.Headers.Add("Link", "</v2/_catalog?n=50>; rel=\"first\"");

        var nextLink = RegistryRepository.ExtractNextLink(response.Headers);

        Assert.Null(nextLink);
    }

    [Fact]
    public void ExtractNextLink_MissingHeader_ReturnsNull()
    {
        using var response = new HttpResponseMessage();

        var nextLink = RegistryRepository.ExtractNextLink(response.Headers);

        Assert.Null(nextLink);
    }
}
