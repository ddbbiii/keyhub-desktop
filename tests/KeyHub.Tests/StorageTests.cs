using System.Text;
using KeyHub.Core.Models;

namespace KeyHub.Tests;

public sealed class StorageTests
{
    [Fact]
    public void SecretRoundTripUsesDpapiAndDatabaseDoesNotContainPlaintext()
    {
        using var test = new TestStore();
        const string plaintext = "super-secret-value-6c42b4d7";

        var saved = test.Store.SaveSecret(null, "Test API", SecretKind.ApiKey, plaintext, tags: "test");
        var revealed = test.Store.RevealSecret(saved.Id);

        Assert.Equal(plaintext, revealed.Value);
        var databaseBytes = File.ReadAllBytes(test.Store.DatabasePath);
        Assert.DoesNotContain(plaintext, Encoding.UTF8.GetString(databaseBytes), StringComparison.Ordinal);
        Assert.DoesNotContain(test.Store.ListSecrets(), x => x.GetType().GetProperty("Value") is not null);
    }

    [Fact]
    public void ProjectBindingsResolveOnlyMappedValues()
    {
        using var test = new TestStore();
        var first = test.Store.SaveSecret(null, "First", SecretKind.Token, "one");
        test.Store.SaveSecret(null, "Second", SecretKind.Token, "two");
        test.Store.SaveProject("demo", "Demo", "", "");
        test.Store.SetBinding("demo", "DEMO_TOKEN", first.Id);

        var values = test.Store.ResolveEnvironment("demo");

        Assert.Equal("one", values["DEMO_TOKEN"]);
        Assert.Single(values);
    }

    [Fact]
    public void BoundSecretCannotBeDeleted()
    {
        using var test = new TestStore();
        var secret = test.Store.SaveSecret(null, "Bound", SecretKind.Token, "value");
        test.Store.SaveProject("demo", "Demo", "", "");
        test.Store.SetBinding("demo", "BOUND_TOKEN", secret.Id);

        Assert.ThrowsAny<Exception>(() => test.Store.DeleteSecret(secret.Id));
        Assert.Equal("value", test.Store.RevealSecret(secret.Id).Value);
    }
}
