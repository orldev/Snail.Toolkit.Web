using Snail.Toolkit.Web.Identity;

namespace Snail.Toolkit.Web.Scraping.Stealth;

/// <summary>
/// Aligns what scripts can observe with the identity the context was opened under.
/// </summary>
/// <remarks>
/// Three defects made an earlier script worse than injecting nothing. It restored the original
/// <c>getParameter</c> without its receiver, so every WebGL query that was not one of the two spoofed
/// parameters threw <c>Illegal invocation</c>. It added canvas noise as <c>+= Math.random() * 0.1</c> to a
/// <c>Uint8ClampedArray</c>, where anything below half a unit rounds back to the original byte, so no pixel
/// ever changed. And every patch ran in one unguarded block, so the missing <c>navigator.permissions</c> of a
/// non-secure context aborted the script and silently dropped every patch below it — including the
/// <c>webdriver</c> override the whole exercise exists for.
/// </remarks>
public sealed class StealthScript : IStealthScript
{
    /// <inheritdoc />
    public string For(WebIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var languages = string.Join(", ", identity.Languages.Select(tag => $"'{tag}'"));

        return $$"""
            (() => {
                const guard = (patch) => { try { patch(); } catch (error) { } };

                const define = (name, value) => {
                    const prototype = Object.getPrototypeOf(navigator);
                    const holder = Object.getOwnPropertyDescriptor(prototype, name) ? prototype : navigator;
                    Object.defineProperty(holder, name, {
                        get: () => value, configurable: true, enumerable: true
                    });
                };

                guard(() => define('webdriver', false));
                guard(() => define('languages', [{{languages}}]));
                guard(() => define('platform', '{{identity.NavigatorPlatform}}'));
                guard(() => define('hardwareConcurrency', {{identity.HardwareConcurrency}}));
                guard(() => define('deviceMemory', {{identity.DeviceMemory}}));

                guard(() => define('connection', {
                    effectiveType: '4g', rtt: 50, downlink: 10, saveData: false
                }));

                guard(() => {
                    const pluginList = [
                        { name: 'PDF Viewer', filename: 'internal-pdf-viewer' },
                        { name: 'Chrome PDF Viewer', filename: 'internal-pdf-viewer' },
                        { name: 'Chromium PDF Viewer', filename: 'internal-pdf-viewer' }
                    ];
                    pluginList.item = (index) => pluginList[index];
                    pluginList.namedItem = (name) => pluginList.find((plugin) => plugin.name === name);
                    define('plugins', pluginList);
                });

                guard(() => {
                    const original = WebGLRenderingContext.prototype.getParameter;
                    const spoofed = { 37445: 'Intel Inc.', 37446: 'Intel Iris OpenGL Engine' };
                    WebGLRenderingContext.prototype.getParameter = function (parameter) {
                        if (parameter in spoofed) return spoofed[parameter];
                        return original.apply(this, arguments);
                    };
                });

                guard(() => {
                    const seed = {{Seed(identity)}};
                    const shift = (canvas) => {
                        const context = canvas.getContext('2d');
                        if (!context || canvas.width === 0 || canvas.height === 0) return;
                        const image = context.getImageData(0, 0, canvas.width, canvas.height);
                        for (let i = 0; i < image.data.length; i += 4) {
                            if ((i + seed) % 977 !== 0) continue;
                            image.data[i] = image.data[i] ^ 1;
                        }
                        context.putImageData(image, 0, 0);
                    };
                    const originalToDataUrl = HTMLCanvasElement.prototype.toDataURL;
                    HTMLCanvasElement.prototype.toDataURL = function () {
                        guard(() => shift(this));
                        return originalToDataUrl.apply(this, arguments);
                    };
                });

                guard(() => {
                    const query = window.navigator.permissions.query.bind(window.navigator.permissions);
                    window.navigator.permissions.query = (parameters) =>
                        parameters.name === 'notifications'
                            ? Promise.resolve({ state: Notification.permission })
                            : query(parameters);
                });

                guard(() => {
                    window.chrome = window.chrome || {};
                    window.chrome.runtime = window.chrome.runtime || {};
                    window.chrome.app = window.chrome.app || { isInstalled: false };
                });
            })();
            """;
    }

    private static int Seed(WebIdentity identity)
        => Math.Abs(StringComparer.Ordinal.GetHashCode(identity.UserAgent) % 512) + 1;
}
