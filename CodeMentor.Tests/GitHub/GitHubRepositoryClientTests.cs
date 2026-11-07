using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodeMentor.Infrastructure;
using CodeMentor.Application.GitHub;
using CodeMentor.Infrastructure.GitHub;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace CodeMentor.Tests.GitHub;

public sealed class GitHubRepositoryClientTests
{
    [Fact]
    public async Task GetPublicRepositoryAsync_SendsExpectedHeadersToFixedApiHostAndReturnsMetadata()
    {
        HttpRequestMessage? capturedRequest = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, PublicRepositoryJson));
        });
        var client = new GitHubRepositoryClient(httpClient);

        var metadata = await client.GetPublicRepositoryAsync(ParseReference("https://github.com/octo-org/project.git"));

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest.Method);
        Assert.Equal("https://api.github.com/repos/octo-org/project", capturedRequest.RequestUri!.AbsoluteUri);
        Assert.Contains(
            capturedRequest.Headers.UserAgent,
            product => product.Product?.Name == "CodeMentorAI" && product.Product.Version == "1.0");
        Assert.Contains(
            capturedRequest.Headers.Accept,
            mediaType => mediaType.MediaType == "application/vnd.github+json");
        Assert.Equal("2022-11-28", Assert.Single(capturedRequest.Headers.GetValues("X-GitHub-Api-Version")));
        Assert.Equal("octo-org", metadata.Owner);
        Assert.Equal("project", metadata.Name);
        Assert.Equal("https://github.com/octo-org/project", metadata.CanonicalHtmlUrl);
        Assert.Equal("main", metadata.DefaultBranch);
        Assert.Equal("public", metadata.Visibility);
    }

    [Fact]
    public async Task GetPublicRepositoryAsync_RejectsRepositoryMarkedPrivate()
    {
        const string privateRepositoryJson = """
            {
              "owner": { "login": "octo-org" },
              "name": "project",
              "html_url": "https://github.com/octo-org/project",
              "default_branch": "main",
              "visibility": "public",
              "private": true
            }
            """;
        var client = CreateClient((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, privateRepositoryJson)));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetPublicRepositoryAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(GitHubRepositoryClientError.NotPublic, exception.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, GitHubRepositoryClientError.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, GitHubRepositoryClientError.ServerError)]
    [InlineData(HttpStatusCode.BadGateway, GitHubRepositoryClientError.ServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, GitHubRepositoryClientError.ServerError)]
    [InlineData(HttpStatusCode.BadRequest, GitHubRepositoryClientError.UnexpectedStatus)]
    public async Task GetPublicRepositoryAsync_MapsHttpFailures(
        HttpStatusCode statusCode,
        GitHubRepositoryClientError expectedError)
    {
        var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(statusCode)));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetPublicRepositoryAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(expectedError, exception.Error);
        Assert.Equal(statusCode, exception.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData((HttpStatusCode)429)]
    public async Task GetPublicRepositoryAsync_CapturesRateLimitRetryInformation(HttpStatusCode statusCode)
    {
        var resetAt = DateTimeOffset.UtcNow.AddMinutes(3).ToUnixTimeSeconds();
        var response = new HttpResponseMessage(statusCode);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
        response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", resetAt.ToString());
        var client = CreateClient((_, _) => Task.FromResult(response));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetPublicRepositoryAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(GitHubRepositoryClientError.RateLimited, exception.Error);
        Assert.Equal(statusCode, exception.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(45), exception.RetryAfterDelay);
        Assert.Null(exception.RetryAfterAtUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(resetAt), exception.RateLimitResetAtUtc);
    }

    [Fact]
    public async Task GetPublicRepositoryAsync_CapturesRetryAfterHttpDate()
    {
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(2);
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAt);
        var client = CreateClient((_, _) => Task.FromResult(response));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetPublicRepositoryAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(retryAt, exception.RetryAfterAtUtc);
        Assert.NotNull(exception.RetryAfterDelay);
        Assert.True(exception.RetryAfterDelay > TimeSpan.Zero);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"owner":{"login":"octo-org"},"name":"project","html_url":"https://github.com/octo-org/project","default_branch":"main"}""")]
    [InlineData("""{"owner":{"login":"octo-org"},"name":"project","html_url":"https://github.com/octo-org/project","default_branch":"main","visibility":null}""")]
    [InlineData("""{"name":"project","html_url":"https://github.com/octo-org/project","default_branch":"main","visibility":"public"}""")]
    [InlineData("""{"owner":{},"name":"project","html_url":"https://github.com/octo-org/project","default_branch":"main","visibility":"public"}""")]
    [InlineData("""{"owner":{"login":"octo-org"},"html_url":"https://github.com/octo-org/project","default_branch":"main","visibility":"public"}""")]
    [InlineData("""{"owner":{"login":"octo-org"},"name":"project","default_branch":"main","visibility":"public"}""")]
    [InlineData("""{"owner":{"login":"octo-org"},"name":"project","html_url":"https://github.com/octo-org/project","visibility":"public"}""")]
    [InlineData("""{"owner":{"login":"octo-org"},"name":"other","html_url":"https://github.com/octo-org/project","default_branch":"main","visibility":"public"}""")]
    [InlineData("""{"owner":{"login":"octo-org"},"name":"project","html_url":"https://attacker.example/octo-org/project","default_branch":"main","visibility":"public"}""")]
    [InlineData("""{"owner":{"login":"octo-org"},"name":"project","html_url":"https://github.com/octo-org/project","default_branch":"main","visibility":"public","private":"false"}""")]
    public async Task GetPublicRepositoryAsync_RejectsMalformedOrInconsistentMetadata(string json)
    {
        var client = CreateClient((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, json)));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetPublicRepositoryAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(GitHubRepositoryClientError.InvalidResponse, exception.Error);
        Assert.DoesNotContain("attacker.example", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPublicRepositoryAsync_RejectsNonPublicVisibility()
    {
        const string privateRepositoryJson = """
            {
              "owner": { "login": "octo-org" },
              "name": "project",
              "html_url": "https://github.com/octo-org/project",
              "default_branch": "main",
              "visibility": "private"
            }
            """;
        var client = CreateClient((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, privateRepositoryJson)));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetPublicRepositoryAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(GitHubRepositoryClientError.NotPublic, exception.Error);
    }

    [Fact]
    public async Task GetPublicRepositoryAsync_MapsNetworkFailureWithoutResponseContent()
    {
        var client = CreateClient((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("sensitive transport details")));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetPublicRepositoryAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(GitHubRepositoryClientError.NetworkError, exception.Error);
        Assert.DoesNotContain("sensitive transport details", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPublicRepositoryAsync_MapsHttpClientTimeout()
    {
        var client = CreateClient((_, _) =>
            Task.FromException<HttpResponseMessage>(
                new OperationCanceledException("timeout", new TimeoutException(), CancellationToken.None)));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetPublicRepositoryAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(GitHubRepositoryClientError.Timeout, exception.Error);
    }

    [Fact]
    public async Task GetPublicRepositoryAsync_PropagatesCallerCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync();
        var client = CreateClient((_, cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetPublicRepositoryAsync(
                ParseReference("https://github.com/octo-org/project"),
                cancellationSource.Token));
    }

    [Fact]
    public async Task GetRepositoryTreeAsync_ResolvesDefaultBranchAndReturnsAllEntryTypes()
    {
        var requests = new List<Uri>();
        var tree = await CreateTreeClient((request, _) =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri);
            return Task.FromResult(uri.AbsolutePath switch
            {
                "/repos/octo-org/project" => JsonResponse(HttpStatusCode.OK, PublicRepositoryJson),
                "/repos/octo-org/project/git/ref/heads/main" =>
                    JsonResponse(HttpStatusCode.OK, ReferenceJson),
                $"/repos/octo-org/project/git/commits/{CommitSha}" =>
                    JsonResponse(HttpStatusCode.OK, CommitJson),
                $"/repos/octo-org/project/git/trees/{TreeSha}" =>
                    JsonResponse(HttpStatusCode.OK, CompleteTreeJson),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        }).GetRepositoryTreeAsync(ParseReference("https://github.com/octo-org/project"));

        Assert.Equal(4, requests.Count);
        Assert.All(requests, uri => Assert.Equal("api.github.com", uri.Host));
        Assert.Equal("/repos/octo-org/project/git/ref/heads/main", requests[1].AbsolutePath);
        Assert.Equal($"/repos/octo-org/project/git/commits/{CommitSha}", requests[2].AbsolutePath);
        Assert.Equal($"/repos/octo-org/project/git/trees/{TreeSha}", requests[3].AbsolutePath);
        Assert.Equal("?recursive=1", requests[3].Query);
        Assert.Equal(TreeSha, tree.Sha);
        Assert.Collection(
            tree.Entries,
            blob =>
            {
                Assert.Equal("src/Program.cs", blob.Path);
                Assert.Equal(GitHubRepositoryTreeEntryType.Blob, blob.Type);
                Assert.Equal(123, blob.Size);
                Assert.Equal(CommitSha, blob.Sha);
            },
            folder =>
            {
                Assert.Equal("src", folder.Path);
                Assert.Equal(GitHubRepositoryTreeEntryType.Tree, folder.Type);
                Assert.Null(folder.Size);
            },
            submodule =>
            {
                Assert.Equal("vendor/library", submodule.Path);
                Assert.Equal(GitHubRepositoryTreeEntryType.Commit, submodule.Type);
                Assert.Null(submodule.Size);
            });
    }

    [Fact]
    public async Task GetRepositoryTreeAsync_EscapesValidatedDefaultBranchSegments()
    {
        var requests = new List<string>();
        var customBranchMetadata = PublicRepositoryJson.Replace(
            "\"default_branch\": \"main\"",
            "\"default_branch\": \"release/2026.11\"",
            StringComparison.Ordinal);
        var client = CreateTreeClient((request, _) =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/repos/octo-org/project" => JsonResponse(HttpStatusCode.OK, customBranchMetadata),
                "/repos/octo-org/project/git/ref/heads/release/2026.11" =>
                    JsonResponse(HttpStatusCode.OK, ReferenceJson),
                $"/repos/octo-org/project/git/commits/{CommitSha}" =>
                    JsonResponse(HttpStatusCode.OK, CommitJson),
                $"/repos/octo-org/project/git/trees/{TreeSha}" =>
                    JsonResponse(HttpStatusCode.OK, CompleteTreeJson),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        });

        await client.GetRepositoryTreeAsync(ParseReference("https://github.com/octo-org/project"));

        Assert.Contains("/repos/octo-org/project/git/ref/heads/release/2026.11", requests);
    }

    [Theory]
    [InlineData("""{"truncated":false,"tree":[]}""")]
    [InlineData("""{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","truncated":"false","tree":[]}""")]
    [InlineData("""{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","truncated":false,"tree":{}}""")]
    [InlineData("""{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","truncated":false,"tree":[{"path":"/rooted","type":"blob","sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]}""")]
    [InlineData("""{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","truncated":false,"tree":[{"path":"C:/rooted","type":"blob","sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]}""")]
    [InlineData("""{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","truncated":false,"tree":[{"path":"../escape","type":"blob","sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]}""")]
    [InlineData("""{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","truncated":false,"tree":[{"path":"src/../../escape","type":"blob","sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]}""")]
    [InlineData("""{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","truncated":false,"tree":[{"path":"src\\file.cs","type":"blob","sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]}""")]
    [InlineData("""{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","truncated":false,"tree":[{"path":"file.cs","type":"unknown","sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]}""")]
    [InlineData("""{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","truncated":false,"tree":[{"path":"file.cs","type":"blob","sha":"not-a-sha"}]}""")]
    [InlineData("""{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","truncated":false,"tree":[{"path":"file.cs","type":"blob","size":-1,"sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]}""")]
    public async Task GetRepositoryTreeAsync_RejectsMalformedTreeResponse(string json)
    {
        var client = CreateTreeClient((request, _) =>
            Task.FromResult(GetTreeRouteResponse(request, json)));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryTreeAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(GitHubRepositoryClientError.InvalidResponse, exception.Error);
    }

    [Fact]
    public async Task GetRepositoryTreeAsync_ReportsTruncatedResponseWithoutReturningEntries()
    {
        const string truncatedTreeJson = """
            {
              "sha": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "truncated": true,
              "tree": [{"path":"partial.cs","type":"blob","sha":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]
            }
            """;
        var client = CreateTreeClient((request, _) =>
            Task.FromResult(GetTreeRouteResponse(request, truncatedTreeJson)));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryTreeAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(GitHubRepositoryClientError.TreeTruncated, exception.Error);
    }

    [Fact]
    public async Task GetRepositoryTreeAsync_RejectsTreeResponseExceedingByteLimit()
    {
        var client = CreateTreeClient(
            (request, _) => Task.FromResult(GetTreeRouteResponse(request, CompleteTreeJson)),
            new GitHubRepositoryTreeLimits(maximumResponseBytes: 128, maximumEntries: 100));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryTreeAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(GitHubRepositoryClientError.TreeTooLarge, exception.Error);
    }

    [Fact]
    public async Task GetRepositoryTreeAsync_RejectsTreeExceedingEntryLimit()
    {
        var client = CreateTreeClient(
            (request, _) => Task.FromResult(GetTreeRouteResponse(request, CompleteTreeJson)),
            new GitHubRepositoryTreeLimits(maximumResponseBytes: 4096, maximumEntries: 2));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryTreeAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(GitHubRepositoryClientError.TreeTooLarge, exception.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, GitHubRepositoryClientError.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, GitHubRepositoryClientError.RateLimited)]
    [InlineData((HttpStatusCode)429, GitHubRepositoryClientError.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, GitHubRepositoryClientError.ServerError)]
    public async Task GetRepositoryTreeAsync_MapsGitHubHttpErrors(
        HttpStatusCode statusCode,
        GitHubRepositoryClientError expectedError)
    {
        var client = CreateTreeClient((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/repos/octo-org/project")
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, PublicRepositoryJson));
            }

            var response = new HttpResponseMessage(statusCode);
            if (statusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            }

            return Task.FromResult(response);
        });

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryTreeAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(expectedError, exception.Error);
        if (expectedError == GitHubRepositoryClientError.RateLimited)
        {
            Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfterDelay);
        }
    }

    [Fact]
    public async Task GetRepositoryTreeAsync_MapsNetworkFailure()
    {
        var calls = 0;
        var client = CreateTreeClient((request, _) =>
        {
            if (calls++ == 0)
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, PublicRepositoryJson));
            }

            return Task.FromException<HttpResponseMessage>(new HttpRequestException("sensitive network detail"));
        });

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryTreeAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(GitHubRepositoryClientError.NetworkError, exception.Error);
        Assert.DoesNotContain("sensitive network detail", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRepositoryTreeAsync_MapsTimeout()
    {
        var calls = 0;
        var client = CreateTreeClient((_, _) =>
        {
            if (calls++ == 0)
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, PublicRepositoryJson));
            }

            return Task.FromException<HttpResponseMessage>(
                new OperationCanceledException("timeout", new TimeoutException(), CancellationToken.None));
        });

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryTreeAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(GitHubRepositoryClientError.Timeout, exception.Error);
    }

    [Fact]
    public async Task GetRepositoryTreeAsync_PropagatesCallerCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        var calls = 0;
        var client = CreateTreeClient((_, cancellationToken) =>
        {
            if (calls++ == 0)
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, PublicRepositoryJson));
            }

            cancellationSource.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetRepositoryTreeAsync(
                ParseReference("https://github.com/octo-org/project"),
                cancellationSource.Token));
    }

    [Fact]
    public async Task GetRepositoryTreeAsync_RejectsInvalidDefaultBranchBeforeReferenceRequest()
    {
        var invalidBranchMetadata = PublicRepositoryJson.Replace(
            "\"default_branch\": \"main\"",
            "\"default_branch\": \"../other\"",
            StringComparison.Ordinal);
        var calls = new List<string>();
        var client = CreateTreeClient((request, _) =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(request.RequestUri.AbsolutePath == "/repos/octo-org/project"
                ? JsonResponse(HttpStatusCode.OK, invalidBranchMetadata)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryTreeAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(GitHubRepositoryClientError.InvalidResponse, exception.Error);
        Assert.Single(calls);
    }

    [Theory]
    [InlineData("/git/ref/heads/main", """{"object":{"type":"tree","sha":"1111111111111111111111111111111111111111"}}""")]
    [InlineData("/git/ref/heads/main", """{"object":{"type":"commit","sha":"invalid"}}""")]
    [InlineData("/git/commits/", """{"tree":{"sha":"invalid"}}""")]
    public async Task GetRepositoryTreeAsync_RejectsMalformedReferenceOrCommitResponse(
        string routeSuffix,
        string json)
    {
        var client = CreateTreeClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/repos/octo-org/project")
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, PublicRepositoryJson));
            }

            if (path.EndsWith("/git/ref/heads/main", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(
                    HttpStatusCode.OK,
                    routeSuffix == "/git/ref/heads/main" ? json : ReferenceJson));
            }

            if (path.EndsWith($"/git/commits/{CommitSha}", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse(
                    HttpStatusCode.OK,
                    routeSuffix == "/git/commits/" ? json : CommitJson));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryTreeAsync(ParseReference("https://github.com/octo-org/project")));

        Assert.Equal(GitHubRepositoryClientError.InvalidResponse, exception.Error);
    }

    [Fact]
    public async Task GetRepositoryFilesAsync_FetchesOnlySelectedVerifiedBlobsAndReturnsUtf8Text()
    {
        var tree = CreateRepositoryTree(
            BlobEntry("src/Program.cs", "public class Program {}", 123),
            BlobEntry("README.md", "# Project", 124));
        var requests = new List<Uri>();
        var client = CreateFileClient((request, _) =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri);
            if (IsTreeVerificationRequest(uri))
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, SerializeTree(tree)));
            }

            var entry = tree.Entries.Single(candidate => uri.AbsolutePath.EndsWith(candidate.Sha));
            var content = entry.Path == "src/Program.cs" ? "public class Program {}" : "# Project";
            return Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                SerializeBlob(entry, Encoding.UTF8.GetBytes(content))));
        });

        var files = await client.GetRepositoryFilesAsync(
            ParseReference("https://github.com/octo-org/project"),
            tree,
            ["src/Program.cs", "README.md"]);

        Assert.Equal(3, requests.Count);
        Assert.All(requests, uri => Assert.Equal("api.github.com", uri.Host));
        Assert.Equal($"/repos/octo-org/project/git/blobs/{tree.Entries[0].Sha}", requests[1].AbsolutePath);
        Assert.Equal($"/repos/octo-org/project/git/blobs/{tree.Entries[1].Sha}", requests[2].AbsolutePath);
        Assert.Collection(
            files,
            file =>
            {
                Assert.Equal("src/Program.cs", file.Path);
                Assert.Equal("public class Program {}", file.Content);
            },
            file =>
            {
                Assert.Equal("README.md", file.Path);
                Assert.Equal("# Project", file.Content);
            });
    }

    [Fact]
    public async Task GetRepositoryFilesAsync_RejectsUnverifiedTreeIdentityBeforeRequest()
    {
        var tree = CreateRepositoryTree(BlobEntry("Program.cs", "code", 1)) with
        {
            Owner = "another-owner"
        };
        var requestCount = 0;
        var client = CreateFileClient((_, _) =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryFilesAsync(
                ParseReference("https://github.com/octo-org/project"),
                tree,
                ["Program.cs"]));

        Assert.Equal(GitHubRepositoryClientError.InvalidSelection, exception.Error);
        Assert.Equal(0, requestCount);
    }

    [Theory]
    [InlineData("src/../Program.cs")]
    [InlineData("/Program.cs")]
    [InlineData("C:/Program.cs")]
    [InlineData("src\\Program.cs")]
    [InlineData("missing.cs")]
    [InlineData("src/image.png")]
    [InlineData("config/secrets.json")]
    public async Task GetRepositoryFilesAsync_RejectsInvalidOrDisallowedSelectionBeforeRequest(string path)
    {
        var tree = CreateRepositoryTree(
            BlobEntry("src/Program.cs", "code", 4),
            BlobEntry("config/secrets.json", "secret", 6));
        var requestCount = 0;
        var client = CreateFileClient((_, _) =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryFilesAsync(
                ParseReference("https://github.com/octo-org/project"),
                tree,
                [path]));

        Assert.Equal(GitHubRepositoryClientError.InvalidSelection, exception.Error);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task GetRepositoryFilesAsync_RejectsDuplicateSelectionsBeforeRequest()
    {
        var tree = CreateRepositoryTree(BlobEntry("Program.cs", "code", 4));
        var requestCount = 0;
        var client = CreateFileClient((_, _) =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryFilesAsync(
                ParseReference("https://github.com/octo-org/project"),
                tree,
                ["Program.cs", "Program.cs"]));

        Assert.Equal(GitHubRepositoryClientError.InvalidSelection, exception.Error);
        Assert.Equal(0, requestCount);
    }

    [Theory]
    [InlineData(GitHubRepositoryClientError.TooManyFiles, 4, 10, 10, 2)]
    [InlineData(GitHubRepositoryClientError.FileTooLarge, 2, 10, 6, 10)]
    [InlineData(GitHubRepositoryClientError.TotalDownloadTooLarge, 3, 5, 3, 3)]
    public async Task GetRepositoryFilesAsync_EnforcesSelectionAndDecodedSizeLimitsBeforeRequest(
        GitHubRepositoryClientError expectedError,
        int selectedCount,
        int totalLimit,
        int fileSize,
        int selectionLimit)
    {
        var entries = Enumerable.Range(0, selectedCount)
            .Select(index => BlobEntry($"src/File{index}.cs", new string('x', fileSize), 100 + index))
            .ToArray();
        var tree = CreateRepositoryTree(entries);
        var client = CreateFileClient(
            (_, _) => throw new InvalidOperationException("No requests should be made."),
            new GitHubRepositoryFileContentLimits(
                maximumFileBytes: expectedError == GitHubRepositoryClientError.FileTooLarge ? 5 : 10,
                maximumTotalBytes: totalLimit,
                maximumSelectedFiles: selectionLimit));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryFilesAsync(
                ParseReference("https://github.com/octo-org/project"),
                tree,
                entries.Select(entry => entry.Path).ToArray()));

        Assert.Equal(expectedError, exception.Error);
    }

    [Fact]
    public async Task GetRepositoryFilesAsync_RejectsCallerForgedEntryAgainstVerifiedTree()
    {
        var suppliedTree = CreateRepositoryTree(BlobEntry("Program.cs", "code", 4));
        var serverTree = CreateRepositoryTree(BlobEntry("Program.cs", "code", 5));
        var requests = new List<Uri>();
        var client = CreateFileClient((request, _) =>
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, SerializeTree(serverTree)));
        });

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryFilesAsync(
                ParseReference("https://github.com/octo-org/project"),
                suppliedTree,
                ["Program.cs"]));

        Assert.Equal(GitHubRepositoryClientError.InvalidSelection, exception.Error);
        Assert.Single(requests);
        Assert.Equal("api.github.com", requests[0].Host);
    }

    [Theory]
    [InlineData("utf16", "dGVzdA==", GitHubRepositoryClientError.InvalidResponse)]
    [InlineData("base64", "%%%=", GitHubRepositoryClientError.InvalidResponse)]
    public async Task GetRepositoryFilesAsync_RejectsMalformedBlobMetadataAndEncoding(
        string encoding,
        string content,
        GitHubRepositoryClientError expectedError)
    {
        var bytes = Encoding.UTF8.GetBytes("test");
        var entry = BlobEntry("Program.cs", "test", 4);
        var tree = CreateRepositoryTree(entry);
        var client = CreateFileClient((request, _) =>
            Task.FromResult(IsTreeVerificationRequest(request.RequestUri!)
                ? JsonResponse(HttpStatusCode.OK, SerializeTree(tree))
                : JsonResponse(
                    HttpStatusCode.OK,
                    JsonSerializer.Serialize(new
                    {
                        sha = entry.Sha,
                        size = bytes.Length,
                        encoding,
                        content
                    }))));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryFilesAsync(
                ParseReference("https://github.com/octo-org/project"),
                tree,
                ["Program.cs"]));

        Assert.Equal(expectedError, exception.Error);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("""{"size":4,"encoding":"base64","content":"dGVzdA=="}""")]
    [InlineData("""{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","encoding":"base64","content":"dGVzdA=="}""")]
    [InlineData("""{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":4,"content":"dGVzdA=="}""")]
    [InlineData("""{"sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":4,"encoding":"base64"}""")]
    public async Task GetRepositoryFilesAsync_RejectsMalformedOrIncompleteBlobResponse(string json)
    {
        var entry = BlobEntry("Program.cs", "test", 123);
        var tree = CreateRepositoryTree(entry);
        var client = CreateFileClient((request, _) =>
            Task.FromResult(IsTreeVerificationRequest(request.RequestUri!)
                ? JsonResponse(HttpStatusCode.OK, SerializeTree(tree))
                : JsonResponse(HttpStatusCode.OK, json)));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryFilesAsync(
                ParseReference("https://github.com/octo-org/project"),
                tree,
                ["Program.cs"]));

        Assert.Equal(GitHubRepositoryClientError.InvalidResponse, exception.Error);
    }

    [Fact]
    public async Task GetRepositoryFilesAsync_RejectsBlobSizeThatDiffersFromTreeEntry()
    {
        var entry = BlobEntry("Program.cs", "test", 123);
        var tree = CreateRepositoryTree(entry);
        var client = CreateFileClient((request, _) =>
            Task.FromResult(IsTreeVerificationRequest(request.RequestUri!)
                ? JsonResponse(HttpStatusCode.OK, SerializeTree(tree))
                : JsonResponse(
                    HttpStatusCode.OK,
                    JsonSerializer.Serialize(new
                    {
                        sha = entry.Sha,
                        size = 5,
                        encoding = "base64",
                        content = Convert.ToBase64String(Encoding.UTF8.GetBytes("test"))
                    }))));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryFilesAsync(
                ParseReference("https://github.com/octo-org/project"),
                tree,
                ["Program.cs"]));

        Assert.Equal(GitHubRepositoryClientError.InvalidResponse, exception.Error);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41, 0x00 }, GitHubRepositoryClientError.UnsupportedContent)]
    [InlineData(new byte[] { 0x41, 0x00, 0x42 }, GitHubRepositoryClientError.UnsupportedContent)]
    public async Task GetRepositoryFilesAsync_RejectsBinaryOrUnsupportedText(
        byte[] bytes,
        GitHubRepositoryClientError expectedError)
    {
        var entry = new GitHubRepositoryTreeEntry(
            "Program.cs",
            GitHubRepositoryTreeEntryType.Blob,
            bytes.Length,
            CommitSha);
        var tree = CreateRepositoryTree(entry);
        var client = CreateFileClient((request, _) =>
            Task.FromResult(IsTreeVerificationRequest(request.RequestUri!)
                ? JsonResponse(HttpStatusCode.OK, SerializeTree(tree))
                : JsonResponse(HttpStatusCode.OK, SerializeBlob(entry, bytes))));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryFilesAsync(
                ParseReference("https://github.com/octo-org/project"),
                tree,
                ["Program.cs"]));

        Assert.Equal(expectedError, exception.Error);
    }

    [Fact]
    public async Task GetRepositoryFilesAsync_RejectsBlobResponseByteLimit()
    {
        var entry = BlobEntry("Program.cs", "text", 4);
        var tree = CreateRepositoryTree(entry);
        var client = CreateFileClient(
            (request, _) => Task.FromResult(IsTreeVerificationRequest(request.RequestUri!)
                ? JsonResponse(HttpStatusCode.OK, SerializeTree(tree))
                : JsonResponse(HttpStatusCode.OK, new string('x', 500))),
            new GitHubRepositoryFileContentLimits(maximumBlobResponseBytes: 256));

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryFilesAsync(
                ParseReference("https://github.com/octo-org/project"),
                tree,
                ["Program.cs"]));

        Assert.Equal(GitHubRepositoryClientError.ResponseTooLarge, exception.Error);
    }

    [Fact]
    public async Task GetRepositoryFilesAsync_DoesNotReturnPartialResultsWhenALaterBlobFails()
    {
        var entries = new[]
        {
            BlobEntry("First.cs", "first", 10),
            BlobEntry("Second.cs", "second", 11)
        };
        var tree = CreateRepositoryTree(entries);
        var blobCalls = 0;
        var client = CreateFileClient((request, _) =>
        {
            var uri = request.RequestUri!;
            if (IsTreeVerificationRequest(uri))
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, SerializeTree(tree)));
            }

            if (blobCalls++ == 0)
            {
                return Task.FromResult(JsonResponse(
                    HttpStatusCode.OK,
                    SerializeBlob(entries[0], Encoding.UTF8.GetBytes("first"))));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryFilesAsync(
                ParseReference("https://github.com/octo-org/project"),
                tree,
                entries.Select(entry => entry.Path).ToArray()));

        Assert.Equal(GitHubRepositoryClientError.ServerError, exception.Error);
        Assert.Equal(2, blobCalls);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, GitHubRepositoryClientError.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, GitHubRepositoryClientError.RateLimited)]
    [InlineData((HttpStatusCode)429, GitHubRepositoryClientError.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, GitHubRepositoryClientError.ServerError)]
    [InlineData(HttpStatusCode.Redirect, GitHubRepositoryClientError.UnexpectedStatus)]
    public async Task GetRepositoryFilesAsync_MapsBlobHttpStatusesWithoutFollowingRedirects(
        HttpStatusCode status,
        GitHubRepositoryClientError expectedError)
    {
        var entry = BlobEntry("Program.cs", "text", 4);
        var tree = CreateRepositoryTree(entry);
        var requests = new List<Uri>();
        var client = CreateFileClient((request, _) =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri);
            if (IsTreeVerificationRequest(uri))
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, SerializeTree(tree)));
            }

            var response = new HttpResponseMessage(status);
            if (status is HttpStatusCode.Forbidden or (HttpStatusCode)429)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(15));
            }

            if (status == HttpStatusCode.Redirect)
            {
                response.Headers.Location = new Uri("https://attacker.example/file");
            }

            return Task.FromResult(response);
        });

        var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
            () => client.GetRepositoryFilesAsync(
                ParseReference("https://github.com/octo-org/project"),
                tree,
                ["Program.cs"]));

        Assert.Equal(expectedError, exception.Error);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, uri => Assert.Equal("api.github.com", uri.Host));
        if (expectedError == GitHubRepositoryClientError.RateLimited)
        {
            Assert.Equal(TimeSpan.FromSeconds(15), exception.RetryAfterDelay);
        }
    }

    [Fact]
    public async Task GetRepositoryFilesAsync_MapsNetworkFailureAndTimeout()
    {
        var entry = BlobEntry("Program.cs", "text", 4);
        var tree = CreateRepositoryTree(entry);

        foreach (var (response, expectedError) in new[]
                 {
                     (Task.FromException<HttpResponseMessage>(new HttpRequestException("private details")),
                         GitHubRepositoryClientError.NetworkError),
                     (Task.FromException<HttpResponseMessage>(
                             new OperationCanceledException("timeout", new TimeoutException(), CancellationToken.None)),
                         GitHubRepositoryClientError.Timeout)
                 })
        {
            var calls = 0;
            var client = CreateFileClient((request, _) =>
            {
                if (IsTreeVerificationRequest(request.RequestUri!))
                {
                    return Task.FromResult(JsonResponse(HttpStatusCode.OK, SerializeTree(tree)));
                }

                calls++;
                return response;
            });

            var exception = await Assert.ThrowsAsync<GitHubRepositoryClientException>(
                () => client.GetRepositoryFilesAsync(
                    ParseReference("https://github.com/octo-org/project"),
                    tree,
                    ["Program.cs"]));

            Assert.Equal(expectedError, exception.Error);
            Assert.Equal(1, calls);
            Assert.DoesNotContain("private details", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetRepositoryFilesAsync_PropagatesCallerCancellation()
    {
        var entry = BlobEntry("Program.cs", "text", 4);
        var tree = CreateRepositoryTree(entry);
        using var cancellationSource = new CancellationTokenSource();
        var client = CreateFileClient((request, cancellationToken) =>
        {
            if (IsTreeVerificationRequest(request.RequestUri!))
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, SerializeTree(tree)));
            }

            cancellationSource.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetRepositoryFilesAsync(
                ParseReference("https://github.com/octo-org/project"),
                tree,
                ["Program.cs"],
                cancellationSource.Token));
    }

    [Fact]
    public void AddInfrastructure_DisablesAutomaticHttpRedirectsForGitHubClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure("Server=localhost;Database=CodeMentorTests;Integrated Security=true;TrustServerCertificate=true");
        using var provider = services.BuildServiceProvider();
        Assert.True(provider
            .GetRequiredService<GitHubRepositoryFileSelectionPolicy>()
            .Allows("src/Program.cs"));
        Assert.IsType<GitHubRepositoryClient>(provider.GetRequiredService<IGitHubRepositoryClient>());
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        var names = new[]
        {
            typeof(IGitHubRepositoryClient).Name,
            typeof(IGitHubRepositoryClient).FullName!
        };
        var redirectsDisabled = false;
        foreach (var name in names)
        {
            using var pipeline = factory.CreateHandler(name);
            HttpMessageHandler primaryHandler = pipeline;
            while (primaryHandler is DelegatingHandler delegatingHandler)
            {
                primaryHandler = delegatingHandler.InnerHandler!;
            }

            redirectsDisabled |= primaryHandler switch
            {
                HttpClientHandler httpClientHandler => !httpClientHandler.AllowAutoRedirect,
                SocketsHttpHandler socketsHttpHandler => !socketsHttpHandler.AllowAutoRedirect,
                _ => false
            };
        }

        Assert.True(redirectsDisabled);
    }

    private static GitHubRepositoryClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) =>
        new(CreateHttpClient(sendAsync));

    private static GitHubRepositoryClient CreateTreeClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
        GitHubRepositoryTreeLimits? limits = null) =>
        new(
            CreateHttpClient(sendAsync),
            limits ?? new GitHubRepositoryTreeLimits(),
            new GitHubRepositoryFileContentLimits(),
            new GitHubRepositoryFileSelectionPolicy());

    private static GitHubRepositoryClient CreateFileClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync,
        GitHubRepositoryFileContentLimits? limits = null) =>
        new(
            CreateHttpClient(sendAsync),
            new GitHubRepositoryTreeLimits(),
            limits ?? new GitHubRepositoryFileContentLimits(),
            new GitHubRepositoryFileSelectionPolicy());

    private static GitHubRepositoryTree CreateRepositoryTree(
        params GitHubRepositoryTreeEntry[] entries) =>
        new("octo-org", "project", TreeSha, entries);

    private static GitHubRepositoryTreeEntry BlobEntry(string path, string content, int seed) =>
        new(
            path,
            GitHubRepositoryTreeEntryType.Blob,
            Encoding.UTF8.GetByteCount(content),
            seed.ToString("x").PadLeft(40, 'a'));

    private static string SerializeTree(GitHubRepositoryTree tree) =>
        JsonSerializer.Serialize(new
        {
            sha = tree.Sha,
            truncated = false,
            tree = tree.Entries.Select(entry => new
            {
                path = entry.Path,
                type = entry.Type switch
                {
                    GitHubRepositoryTreeEntryType.Blob => "blob",
                    GitHubRepositoryTreeEntryType.Tree => "tree",
                    GitHubRepositoryTreeEntryType.Commit => "commit",
                    _ => "invalid"
                },
                size = entry.Size,
                sha = entry.Sha
            })
        });

    private static string SerializeBlob(GitHubRepositoryTreeEntry entry, byte[] bytes) =>
        JsonSerializer.Serialize(new
        {
            sha = entry.Sha,
            size = bytes.Length,
            encoding = "base64",
            content = Convert.ToBase64String(bytes)
        });

    private static bool IsTreeVerificationRequest(Uri uri) =>
        uri.AbsolutePath == $"/repos/octo-org/project/git/trees/{TreeSha}" &&
        uri.Query == "?recursive=1";

    private static HttpResponseMessage GetTreeRouteResponse(
        HttpRequestMessage request,
        string treeJson) =>
        request.RequestUri!.AbsolutePath switch
        {
            "/repos/octo-org/project" => JsonResponse(HttpStatusCode.OK, PublicRepositoryJson),
            "/repos/octo-org/project/git/ref/heads/main" =>
                JsonResponse(HttpStatusCode.OK, ReferenceJson),
            $"/repos/octo-org/project/git/commits/{CommitSha}" =>
                JsonResponse(HttpStatusCode.OK, CommitJson),
            $"/repos/octo-org/project/git/trees/{TreeSha}" =>
                JsonResponse(HttpStatusCode.OK, treeJson),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) =>
        new(new FakeHttpMessageHandler(sendAsync));

    private static GitHubRepositoryReference ParseReference(string value)
    {
        Assert.True(GitHubRepositoryReference.TryParse(value, out var reference));
        return reference!;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private const string PublicRepositoryJson = """
        {
          "owner": { "login": "octo-org" },
          "name": "project",
          "html_url": "https://github.com/octo-org/project",
          "default_branch": "main",
          "visibility": "public",
          "private": false
        }
        """;

    private const string CommitSha = "1111111111111111111111111111111111111111";
    private const string TreeSha = "2222222222222222222222222222222222222222";
    private const string ReferenceJson = $$$"""
        {"ref":"refs/heads/main","object":{"type":"commit","sha":"{{{CommitSha}}}"}}
        """;
    private const string CommitJson = $$$"""
        {"sha":"{{{CommitSha}}}","tree":{"sha":"{{{TreeSha}}}"}}
        """;
    private const string CompleteTreeJson = $$$"""
        {
          "sha": "{{{TreeSha}}}",
          "truncated": false,
          "tree": [
            {"path":"src/Program.cs","mode":"100644","type":"blob","size":123,"sha":"{{{CommitSha}}}"},
            {"path":"src","mode":"040000","type":"tree","size":null,"sha":"{{{TreeSha}}}"},
            {"path":"vendor/library","mode":"160000","type":"commit","sha":"{{{CommitSha}}}"}
          ]
        }
        """;

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            sendAsync(request, cancellationToken);
    }
}
