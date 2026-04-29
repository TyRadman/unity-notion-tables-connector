using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(TNotionPropertyAttribute))]
public sealed class TNotionPropertyDrawer : PropertyDrawer
{
    private const string ICON_RESOURCES_PATH = "NotionSyncer/T_Icon_Notion";
    private const float ICON_SIZE = 16f;
    private const float ICON_PADDING = 4f;

    private static Texture2D _icon;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var notionAttribute = (TNotionPropertyAttribute)attribute;

        label.tooltip = $"A value synced with a Notion Database. The property name in Notion is `{notionAttribute.PropertyName}`";

        EditorGUI.BeginProperty(position, label, property);

        Rect indented = EditorGUI.IndentedRect(position);

        Texture2D icon = GetIcon();
        if (icon != null)
        {
            var iconRect = new Rect(
                indented.x, indented.y + (EditorGUIUtility.singleLineHeight - ICON_SIZE) * 0.5f,
                ICON_SIZE, ICON_SIZE);

            GUI.Label(iconRect, new GUIContent(icon, label.tooltip), GUIStyle.none);

            var fieldRect = indented;
            fieldRect.xMin += ICON_SIZE + ICON_PADDING;

            EditorGUI.PropertyField(fieldRect, property, label, true);
        }
        else
        {
            EditorGUI.PropertyField(indented, property, label, true);
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUI.GetPropertyHeight(property, label, true);
    }

    private static Texture2D GetIcon()
    {
        if (_icon == null)
        {
            _icon = Resources.Load<Texture2D>(ICON_RESOURCES_PATH);
        }

        return _icon;
    }
}
