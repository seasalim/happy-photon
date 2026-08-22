namespace HappyPhoton.Services;

internal sealed partial class PreviewBaseCoordinator
{
    private sealed class DecodeSession
    {
        public BaseIdentity Identity { get; }
        public long Generation { get; }
        public long? SurfaceGeneration { get; private set; }
        public CancellationTokenSource Cancellation { get; } = new();
        public Task<BaseImageLoadFailure> Task { get; set; } =
            System.Threading.Tasks.Task.FromResult(
                BaseImageLoadFailure.DecodeFailed);

        public DecodeSession(
            BaseIdentity identity,
            long generation,
            long? surfaceGeneration)
        {
            Identity = identity;
            Generation = generation;
            SurfaceGeneration = surfaceGeneration;
        }

        public void AdoptSurfaceGeneration(long? surfaceGeneration)
        {
            if (surfaceGeneration.HasValue)
            {
                SurfaceGeneration = surfaceGeneration;
            }
        }
    }
}

internal sealed record PreviewBaseResult(
    PreviewBaseLease? Lease,
    BaseImageLoadFailure Failure,
    bool Superseded)
{
    public static PreviewBaseResult Loaded(PreviewBaseLease lease) =>
        new(lease, BaseImageLoadFailure.None, Superseded: false);

    public static PreviewBaseResult Failed(BaseImageLoadFailure failure) =>
        new(null, failure, Superseded: false);

    public static PreviewBaseResult SupersededRequest() =>
        new(null, BaseImageLoadFailure.None, Superseded: true);
}
