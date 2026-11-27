return {
    LrSdkVersion = 13.0,
    -- Masks (SDK 11) and the subject/sky/background/people subtypes (SDK 12)
    -- are the newest calls the plug-in relies on.
    LrSdkMinimumVersion = 12.0,
    LrPluginName = "Lumerist",
    LrToolkitIdentifier = "io.github.ziago.lumerist.lightroom",
    -- Shown in Plug-in Manager: after a restart it confirms which build
    -- Lightroom loaded. Keep in sync with the top-level VERSION file: the Mac
    -- build stamps it (Scripts/plugin-version.zsh), the Windows build ships
    -- this file as it is.
    VERSION = { major = 0, minor = 9, revision = 2, display = "0.9.2 (protocol 4)" },
    LrInitPlugin = "Init.lua",
    -- Without this Lightroom may load the plug-in lazily, on first use (menu,
    -- Plug-in Manager), and the bridge would not listen until then.
    LrForceInitPlugin = true,
    LrShutdownPlugin = "Shutdown.lua",
    LrExportMenuItems = {
        -- Opens the Lumerist app window (starts the app if needed).
        { title = "Open Lumerist", file = "OpenAppMenu.lua" },
    },
}
