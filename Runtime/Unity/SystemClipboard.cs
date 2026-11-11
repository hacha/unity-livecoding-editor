using UnityEngine;

namespace hacha.LiveCodingEditor
{
    /// <summary>OS のクリップボード。</summary>
    public sealed class SystemClipboard : IClipboard
    {
        public string Text
        {
            get => GUIUtility.systemCopyBuffer;
            set => GUIUtility.systemCopyBuffer = value;
        }
    }
}
