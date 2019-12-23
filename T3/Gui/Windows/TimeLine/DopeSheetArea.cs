﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using T3.Core;
using T3.Core.Animation;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Gui.Commands;
using T3.Gui.Graph;
using T3.Gui.Interaction.Snapping;
using T3.Gui.Selection;
using T3.Gui.Styling;
using UiHelpers;

namespace T3.Gui.Windows.TimeLine
{
    public class DopeSheetArea : KeyframeEditArea, ITimeElementSelectionHolder, IValueSnapAttractor
    {
        public DopeSheetArea(ValueSnapHandler snapHandler, TimeLineCanvas timeLineCanvas)
        {
            _snapHandler = snapHandler;
            TimeLineCanvas = timeLineCanvas;
        }

        private GraphWindow.AnimationParameter _currentAnimationParameter;

        public void Draw(Instance compositionOp, List<GraphWindow.AnimationParameter> animationParameters)
        {
            _drawList = ImGui.GetWindowDrawList();
            AnimationParameters = animationParameters;
            _compositionOp = compositionOp;

            ImGui.BeginGroup();
            {
                if (KeyboardBinding.Triggered(UserActions.FocusSelection))
                    ViewAllOrSelectedKeys(alsoChangeTimeRange: true);

                if (KeyboardBinding.Triggered(UserActions.Duplicate))
                    DuplicateSelectedKeyframes();

                ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(0, 3)); // keep some padding 
                _minScreenPos = ImGui.GetCursorScreenPos();

                foreach (var parameter in animationParameters)
                {
                    _currentAnimationParameter = parameter;
                    DrawProperty(parameter);
                }

                DrawContextMenu();
            }
            ImGui.EndGroup();
        }

        private void DrawProperty(GraphWindow.AnimationParameter parameter)
        {
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(ImGui.GetContentRegionAvail().X, LayerHeight - 1);
            _drawList.AddRectFilled(new Vector2(min.X, max.Y),
                                    new Vector2(max.X, max.Y + 1), Color.Black);

            var mousePos = ImGui.GetMousePos();
            var mouseTime = TimeLineCanvas.InverseTransformPositionX(mousePos.X);
            var layerArea = new ImRect(min, max);
            var layerHovered = ImGui.IsWindowHovered() && layerArea.Contains(mousePos);
            if (layerHovered)
            {
                ImGui.BeginTooltip();

                ImGui.PushFont(Fonts.FontBold);
                ImGui.Text(parameter.Input.Input.Name);
                ImGui.PopFont();

                foreach (var curve in parameter.Curves)
                {
                    var v = curve.GetSampledValue(mouseTime);
                    ImGui.Text($"{v:0.00}");
                }

                ImGui.EndTooltip();
            }

            ImGui.PushStyleColor(ImGuiCol.Text, layerHovered ? Color.White.Rgba : Color.Gray);
            ImGui.PushFont(Fonts.FontBold);
            ImGui.Text("  " + parameter.Instance.Symbol.Name);
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.Text("." + parameter.Input.Input.Name);
            ImGui.PopStyleColor();

            if (parameter.Curves.Count() == 4)
            {
                DrawCurveGradient(parameter, layerArea);
            }
            else
            {
                DrawCurveLines(parameter, layerArea);
            }

            HandleCreateNewKeyframes(parameter, layerArea);

            foreach (var curve in parameter.Curves)
            {
                foreach (var pair in curve.GetPointTable())
                {
                    DrawKeyframe(pair.Value, layerArea, parameter);
                }
            }

            ImGui.SetCursorScreenPos(min + new Vector2(0, LayerHeight)); // Next Line
        }

        private void HandleCreateNewKeyframes(GraphWindow.AnimationParameter parameter, ImRect layerArea)
        {
            var hoverNewKeyframe = !ImGui.IsAnyItemActive()
                                   && ImGui.IsWindowHovered()
                                   && ImGui.GetIO().KeyAlt
                                   && layerArea.Contains(ImGui.GetMousePos());
            if (!hoverNewKeyframe)
                return;

            var hoverTime = TimeLineCanvas.Current.InverseTransformPositionX(ImGui.GetIO().MousePos.X);
            _snapHandler.CheckForSnapping(ref hoverTime);

            if (ImGui.IsMouseReleased(0))
            {
                var dragDistance = ImGui.GetIO().MouseDragMaxDistanceAbs[0].Length();
                if (dragDistance < 2)
                {
                    TimeLineCanvas.Current.ClearSelection();

                    foreach (var curve in parameter.Curves)
                    {
                        var value = curve.GetSampledValue(hoverTime);
                        var previousU = curve.GetPreviousU(hoverTime);

                        var key = (previousU != null)
                                      ? curve.GetV(previousU.Value).Clone()
                                      : new VDefinition();

                        key.Value = value;
                        key.U = hoverTime;

                        var oldKey = key;
                        curve.AddOrUpdateV(hoverTime, key);
                        SelectedKeyframes.Add(oldKey);
                        Log.Debug("added new key at " + hoverTime);
                        TimeLineCanvas.Current.ClipTime.Time = hoverTime;
                    }
                }
            }
            else
            {
                var posOnScreen = new Vector2(
                                              TimeLineCanvas.Current.TransformPositionX(hoverTime) - KeyframeIconWidth / 2 + 1,
                                              layerArea.Min.Y);
                Icons.Draw(Icon.KeyFrame, posOnScreen);
            }

            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        private static readonly Color GrayCurveColor = new Color(1f, 1f, 1.0f, 0.3f);

        private readonly Color[] _curveColors =
        {
            new Color(1f, 0.2f, 0.2f, 0.3f),
            new Color(0.1f, 1f, 0.2f, 0.3f),
            new Color(0.1f, 0.4f, 1.0f, 0.5f),
            GrayCurveColor,
        };

        private void DrawCurveLines(GraphWindow.AnimationParameter parameter, ImRect layerArea)
        {
            const float padding = 2;
            // Lines
            var curveIndex = 0;
            foreach (var curve in parameter.Curves)
            {
                var points = curve.GetPointTable();
                var positions = new List<Vector2>();

                var minValue = float.PositiveInfinity;
                var maxValue = float.NegativeInfinity;
                foreach (var (_, vDef) in points)
                {
                    if (minValue > vDef.Value)
                        minValue = (float)vDef.Value;
                    if (maxValue < vDef.Value)
                        maxValue = (float)vDef.Value;
                }

                VDefinition lastVDef = null;
                float lastValue = 0;

                foreach (var (u, vDef) in points)
                {
                    if (lastVDef != null && lastVDef.OutEditMode == VDefinition.EditMode.Constant)
                    {
                        positions.Add(new Vector2(
                                                  TimeLineCanvas.Current.TransformPositionX((float)u) - 1,
                                                  lastValue));
                    }

                    lastValue = Im.Remap((float)vDef.Value, maxValue, minValue, layerArea.Min.Y + padding, layerArea.Max.Y - padding);
                    positions.Add(new Vector2(
                                              TimeLineCanvas.Current.TransformPositionX((float)u),
                                              lastValue));

                    lastVDef = vDef;
                }

                _drawList.AddPolyline(
                                      ref positions.ToArray()[0],
                                      positions.Count,
                                      parameter.Curves.Count() > 1 ? _curveColors[curveIndex % 4] : GrayCurveColor,
                                      false,
                                      2);
                curveIndex++;
            }
        }

        private void DrawCurveGradient(GraphWindow.AnimationParameter parameter, ImRect layerArea)
        {
            if (parameter.Curves.Count() != 4)
                return;

            var curve = parameter.Curves.First();
            const float padding = 2;

            var points = curve.GetVDefinitions();
            var times = new float[points.Count];
            var colors = new Color[points.Count];

            var curves = parameter.Curves.ToList();

            var index = 0;
            foreach (var vDef in points)
            {
                times[index] = TimeLineCanvas.Current.TransformPositionX((float)vDef.U);
                colors[index] = new Color(
                                          (float)vDef.Value,
                                          (float)curves[1].GetSampledValue(vDef.U),
                                          (float)curves[2].GetSampledValue(vDef.U),
                                          (float)curves[3].GetSampledValue(vDef.U)
                                         );
                index++;
            }

            for (var index2 = 0; index2 < times.Length - 1; index2++)
            {
                _drawList.AddRectFilledMultiColor(new Vector2(times[index2], layerArea.Min.Y + padding),
                                                  new Vector2(times[index2 + 1], layerArea.Max.Y - padding),
                                                  colors[index2],
                                                  colors[index2 + 1],
                                                  colors[index2 + 1],
                                                  colors[index2]);
            }
        }

        private void DrawKeyframe(VDefinition vDef, ImRect layerArea, GraphWindow.AnimationParameter parameter)
        {
            var posOnScreen = new Vector2(
                                          TimeLineCanvas.Current.TransformPositionX((float)vDef.U) - KeyframeIconWidth / 2 + 1,
                                          layerArea.Min.Y);
            ImGui.PushID(vDef.GetHashCode());
            {
                var isSelected = SelectedKeyframes.Contains(vDef);
                Icons.Draw(isSelected ? Icon.KeyFrameSelected : Icon.KeyFrame, posOnScreen);
                ImGui.SetCursorScreenPos(posOnScreen);

                // Click released
                if (ImGui.InvisibleButton("##key", new Vector2(10, 24)))
                {
                    var justClicked = ImGui.GetMouseDragDelta().LengthSquared() < 1;
                    if (justClicked && Math.Abs(TimeLineCanvas.ClipTime.PlaybackSpeed) < 0.001f)
                        TimeLineCanvas.Current.ClipTime.Time = vDef.U;

                    TimeLineCanvas.Current.CompleteDragCommand();

                    if (_changeKeyframesCommand != null)
                    {
                        _changeKeyframesCommand.StoreCurrentValues();
                        UndoRedoStack.Add(_changeKeyframesCommand);
                        _changeKeyframesCommand = null;
                    }
                }

                HandleKeyframeDragging(vDef, isSelected);
                ImGui.PopID();
            }
        }

        private void HandleKeyframeDragging(VDefinition vDef, bool isSelected)
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
            }

            if (!ImGui.IsItemActive() || !ImGui.IsMouseDragging(0, 0f))
                return;

            // Deselect
            if (ImGui.GetIO().KeyCtrl)
            {
                if (!isSelected)
                    return;

                foreach (var k in FindParameterKeysAtPosition(vDef.U))
                {
                    SelectedKeyframes.Remove(k);
                }

                return;
            }

            if (!isSelected)
            {
                if (!ImGui.GetIO().KeyShift)
                {
                    TimeLineCanvas.Current.ClearSelection();
                }

                foreach (var k in FindParameterKeysAtPosition(vDef.U))
                {
                    SelectedKeyframes.Add(k);
                }
            }

            if (_changeKeyframesCommand == null)
            {
                TimeLineCanvas.Current.StartDragCommand();
            }

            var newDragTime = TimeLineCanvas.Current.InverseTransformPositionX(ImGui.GetIO().MousePos.X);
            _snapHandler.CheckForSnapping(ref newDragTime);

            TimeLineCanvas.Current.UpdateDragCommand(newDragTime - vDef.U, 0);
        }

        private IEnumerable<VDefinition> FindParameterKeysAtPosition(double u)
        {
            foreach (var curve in _currentAnimationParameter.Curves)
            {
                var matchingKey = curve.GetVDefinitions().FirstOrDefault(vDef2 => Math.Abs(vDef2.U - u) < 1 / 120f);
                if (matchingKey != null)
                    yield return matchingKey;
            }
        }

        #region implement selection holder interface --------------------------------------------
        void ITimeElementSelectionHolder.ClearSelection()
        {
            SelectedKeyframes.Clear();
        }

        public void UpdateSelectionForArea(ImRect screenArea, SelectMode selectMode)
        {
            if (selectMode == SelectMode.Replace)
                SelectedKeyframes.Clear();

            var startTime = TimeLineCanvas.Current.InverseTransformPositionX(screenArea.Min.X);
            var endTime = TimeLineCanvas.Current.InverseTransformPositionX(screenArea.Max.X);

            var layerMinIndex = (screenArea.Min.Y - _minScreenPos.Y) / LayerHeight - 1;
            var layerMaxIndex = (screenArea.Max.Y - _minScreenPos.Y) / LayerHeight;

            var index = 0;
            foreach (var parameter in AnimationParameters)
            {
                if (index >= layerMinIndex && index <= layerMaxIndex)
                {
                    foreach (var c in parameter.Curves)
                    {
                        var matchingItems = c.GetPointTable()
                                             .Select(pair => pair.Value)
                                             .ToList()
                                             .FindAll(key => key.U <= endTime && key.U >= startTime);
                        switch (selectMode)
                        {
                            case SelectMode.Add:
                            case SelectMode.Replace:
                                SelectedKeyframes.UnionWith(matchingItems);
                                break;
                            case SelectMode.Remove:
                                SelectedKeyframes.ExceptWith(matchingItems);
                                break;
                        }
                    }
                }

                index++;
            }
        }

        ICommand ITimeElementSelectionHolder.StartDragCommand()
        {
            _changeKeyframesCommand = new ChangeKeyframesCommand(_compositionOp.Symbol.Id, SelectedKeyframes);
            return _changeKeyframesCommand;
        }

        void ITimeElementSelectionHolder.UpdateDragCommand(double dt, double dv)
        {
            foreach (var vDefinition in SelectedKeyframes)
            {
                vDefinition.U += dt;
            }

            RebuildCurveTables();
        }

        void ITimeElementSelectionHolder.UpdateDragStartCommand(double dt, double dv)
        {
        }

        void ITimeElementSelectionHolder.UpdateDragEndCommand(double dt, double dv)
        {
        }

        void ITimeElementSelectionHolder.CompleteDragCommand()
        {
            if (_changeKeyframesCommand == null)
                return;

            _changeKeyframesCommand.StoreCurrentValues();
            UndoRedoStack.Add(_changeKeyframesCommand);
            _changeKeyframesCommand = null;
        }

        void ITimeElementSelectionHolder.DeleteSelectedElements()
        {
            KeyframeOperations.DeleteSelectedKeyframesFromAnimationParameters(SelectedKeyframes, AnimationParameters);
            RebuildCurveTables();
        }
        #endregion

        #region implement snapping interface -----------------------------------
        private const float SnapDistance = 6;
        private double _snapThresholdOnCanvas;

        /// <summary>
        /// Snap to all non-selected Clips
        /// </summary>
        SnapResult IValueSnapAttractor.CheckForSnap(double targetTime)
        {
            _snapThresholdOnCanvas = TimeLineCanvas.Current.InverseTransformDirection(new Vector2(SnapDistance, 0)).X;
            var maxForce = 0.0;
            var bestSnapTime = double.NaN;

            foreach (var vDefinition in GetAllKeyframes())
            {
                if (SelectedKeyframes.Contains(vDefinition))
                    continue;

                CheckForSnapping(targetTime, vDefinition.U, maxForce: ref maxForce, bestSnapTime: ref bestSnapTime);
            }

            return double.IsNaN(bestSnapTime)
                       ? null
                       : new SnapResult(bestSnapTime, maxForce);
        }

        private void CheckForSnapping(double targetTime, double anchorTime, ref double maxForce, ref double bestSnapTime)
        {
            var distance = Math.Abs(anchorTime - targetTime);
            if (distance < 0.001)
                return;

            var force = Math.Max(0, _snapThresholdOnCanvas - distance);
            if (force <= maxForce)
                return;

            bestSnapTime = anchorTime;
            maxForce = force;
        }
        #endregion

        private const float KeyframeIconWidth = 10;
        private Vector2 _minScreenPos;
        private static ChangeKeyframesCommand _changeKeyframesCommand;
        private const int LayerHeight = 25;
        private Instance _compositionOp;
        private ImDrawListPtr _drawList;
        private readonly ValueSnapHandler _snapHandler;
    }
}