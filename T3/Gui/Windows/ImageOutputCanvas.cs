using ImGuiNET;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.Numerics;
using T3.Core;
using T3.Core.Logging;
using T3.Gui.Graph;
using T3.Gui.Selection;
using UiHelpers;

namespace T3.Gui.Windows
{
    public class ImageOutputCanvas : ScalableCanvas
    {
        public override IEnumerable<ISelectableNode> SelectableChildren => new List<ISelectableNode>();
        public override SelectionHandler SelectionHandler => new SelectionHandler();

        public void Draw()
        {
            Current = this;
            UpdateCanvas();
            UpdateViewMode();
        }

        /// <summary>
        /// The image canvas that is currently being drawn from the UI.
        /// Note that <see cref="ImageOutputCanvas"/> is NOT a singleton so you can't rely on this to be valid outside of the Draw()ing context.
        /// </summary>
        public static ImageOutputCanvas Current = null;


        public void DrawTexture(Texture2D texture)
        {
            if (texture == null)
                return;

            if (_srv == null || _srv.Resource.NativePointer != texture.NativePointer)
            {
                _srv?.Dispose();
                try
                {
                    _srv = new ShaderResourceView(ResourceManager.Instance().Device, texture);
                }
                catch (Exception e)
                {
                    _srv = null;
                    Log.Warning("ImageOutputCanvas::DrawTexture(...) - Could not create ShaderResourceView for texture.");
                    Log.Warning(e.Message);
                    return;
                }
            }

            var size = new Vector2(texture.Description.Width, texture.Description.Height);
            var area = new ImRect(0, 0, size.X, size.Y);

            if (_viewMode == Modes.Fitted)
                ImageOutputCanvas.Current.FitAreaOnCanvas(area);

            var topLeft = Vector2.Zero;
            var topLeftOnScreen = ImageOutputCanvas.Current.TransformPosition(topLeft);
            ImGui.SetCursorScreenPos(topLeftOnScreen);

            var sizeOnScreen = ImageOutputCanvas.Current.TransformDirection(size);
            ImGui.Image((IntPtr)_srv, sizeOnScreen);

            var description = $"{size.X}x{size.Y}  {_srv.Description.Format}";
            var descriptionWidth = ImGui.CalcTextSize(description).X;

            ImGui.SetCursorScreenPos(new Vector2(
                WindowPos.X + (WindowSize.X - descriptionWidth) / 2,
                WindowPos.Y + WindowSize.Y - 20
                ));
            ImGui.Text(description);
        }


        public void SetViewMode(Modes newMode)
        {
            _viewMode = newMode;
            if (newMode == Modes.Pixel)
            {
                SetScaleToMatchPixels();
            }
        }


        /// <summary>
        /// Updated the view mode if user interacted 
        /// </summary>
        private void UpdateViewMode()
        {
            switch (_viewMode)
            {
                case Modes.Fitted:
                    if (UserScrolledCanvas || UserZoomedCanvas)
                        _viewMode = Modes.Custom;
                    break;

                case Modes.Pixel:
                    if (UserZoomedCanvas)
                        _viewMode = Modes.Custom;
                    break;
            }
        }


        public enum Modes
        {
            Fitted,
            Pixel,
            Custom,
        }
        private Modes _viewMode = Modes.Fitted;


        private ShaderResourceView _srv;
    }
}
