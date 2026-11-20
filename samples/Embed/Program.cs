using System.Globalization;
using LibMpv.Managed;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length is < 1 or > 2)
        {
            MessageBox.Show("Usage: Embed <media-file> [absolute-libmpv-DLL-path]");
            return;
        }

        using var window = new Form { Text = "LibMpv.Managed", Width = 900, Height = 550 };
        var video = new Panel { Dock = DockStyle.Fill };
        window.Controls.Add(video);
        MpvClient? player = null;
        window.Shown += async (_, _) =>
        {
            try
            {
                player = MpvClient.Create(new MpvClientOptions
                {
                    LibraryPath = args.ElementAtOrDefault(1),
                    Options = new Dictionary<string, string>
                    {
                        ["wid"] = video.Handle.ToInt64().ToString(CultureInfo.InvariantCulture),
                        ["vo"] = "gpu",
                        ["gpu-api"] = "opengl",
                    },
                });
                await player.CommandAsync(["loadfile", Path.GetFullPath(args[0]), "replace"]);
                await foreach (var item in player.ReadEventsAsync())
                {
                    if (item is MpvEvent.Ended { Reason: MpvEndReason.Error } ended && !window.IsDisposed)
                        MessageBox.Show(window, $"Playback failed with mpv error {ended.ErrorCode}.");
                }
            }
            catch (ObjectDisposedException) when (window.IsDisposed) { }
            catch (Exception error) when (!window.IsDisposed)
            {
                MessageBox.Show(window, error.Message);
                window.Close();
            }
        };

        try
        {
            Application.Run(window);
        }
        finally
        {
            player?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
