// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using Windows.Win32.Foundation;
using Windows.Win32.System.WinRT;
using Windows.Win32.System.WinRT.Composition;
using WinRT;
using static Windows.Win32.PInvoke;

namespace WinRTInteropTest;

internal class CompositionHost
{
    private readonly Random random = new();

    private Compositor compositor;
    private DispatcherQueueController dispatcherQueueController;
    private DesktopWindowTarget desktopWindowTarget;

    internal CompositionHost()
    {
    }

    internal static CompositionHost Instance { get; } = new CompositionHost();

    internal void Initialize(HWND hwnd)
    {
        this.EnsureDispatcherQueue();
        this.compositor = new Compositor();

        this.CreateDesktopWindow(hwnd);
        this.CreateCompositionRoot();
    }

    internal void AddElement(float size, float x, float y)
    {
        if (this.desktopWindowTarget.Root == null)
        {
            return;
        }

        VisualCollection visuals = ((ContainerVisual)this.desktopWindowTarget.Root).Children;
        SpriteVisual visual = this.compositor.CreateSpriteVisual();

        SpriteVisual element = this.compositor.CreateSpriteVisual();

        var color = new Windows.UI.Color() { A = 255, R = (byte)this.random.Next(256), G = (byte)this.random.Next(256), B = (byte)this.random.Next(256) };
        element.Brush = this.compositor.CreateColorBrush(color);
        element.Size = new System.Numerics.Vector2(size, size);
        element.Offset = new System.Numerics.Vector3(x, y, 0.0f);

        Vector3KeyFrameAnimation animation = this.compositor.CreateVector3KeyFrameAnimation();
        float bottom = 600.0f - element.Size.Y;
        animation.InsertKeyFrame(1, new System.Numerics.Vector3(element.Offset.X, bottom, 0.0f));

        animation.Duration = TimeSpan.FromSeconds(2);
        animation.DelayTime = TimeSpan.FromSeconds(3);

        element.StartAnimation("Offset", animation);
        visuals.InsertAtTop(element);

        visuals.InsertAtTop(visual);
    }

    private void CreateCompositionRoot()
    {
        ContainerVisual root = this.compositor.CreateContainerVisual();
        root.RelativeSizeAdjustment = new System.Numerics.Vector2(1.0f, 1.0f);
        root.Offset = new System.Numerics.Vector3(124, 12, 0);
        this.desktopWindowTarget.Root = root;
    }

    private void CreateDesktopWindow(HWND hwnd)
    {
        ICompositorDesktopInterop interop = this.compositor.As<ICompositorDesktopInterop>();

        interop.CreateDesktopWindowTarget(hwnd, false, out this.desktopWindowTarget);
    }

    private void EnsureDispatcherQueue()
    {
        if (this.dispatcherQueueController != null)
        {
            return;
        }

        var options = new DispatcherQueueOptions()
        {
            dwSize = (uint)Marshal.SizeOf<DispatcherQueueOptions>(),
            apartmentType = DISPATCHERQUEUE_THREAD_APARTMENTTYPE.DQTAT_COM_ASTA,
            threadType = DISPATCHERQUEUE_THREAD_TYPE.DQTYPE_THREAD_CURRENT,
        };

        CreateDispatcherQueueController(options, out this.dispatcherQueueController);
    }
}
