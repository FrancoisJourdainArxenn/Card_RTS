using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;

public static class UgsBootstrap
{
    public static async Task EnsureReadyAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }
}
