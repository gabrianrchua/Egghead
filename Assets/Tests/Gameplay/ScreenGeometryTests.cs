using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class ScreenGeometryTests
{
    private GameObject testObject;

    [TearDown]
    public void TearDown()
    {
        if (testObject != null)
        {
            UnityEngine.Object.DestroyImmediate(testObject);
        }
    }

    [Test]
    public void SafeAreaAppliesChangedGeometryAndSkipsUnchangedGeometry()
    {
        RectTransform rectTransform = CreateSafeArea();
        Rect safeArea = new(100f, 200f, 800f, 1600f);

        Assert.That(RefreshSafeArea(1000, 2000, safeArea), Is.True);
        Assert.That(rectTransform.anchorMin, Is.EqualTo(new Vector2(0.1f, 0.1f)));
        Assert.That(rectTransform.anchorMax, Is.EqualTo(new Vector2(0.9f, 0.9f)));

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;

        Assert.That(RefreshSafeArea(1000, 2000, safeArea), Is.False);
        Assert.That(rectTransform.anchorMin, Is.EqualTo(Vector2.zero));
        Assert.That(rectTransform.anchorMax, Is.EqualTo(Vector2.one));

        Assert.That(RefreshSafeArea(1000, 2000, new Rect(0f, 100f, 1000f, 1800f)), Is.True);
        Assert.That(rectTransform.anchorMin, Is.EqualTo(new Vector2(0f, 0.05f)));
        Assert.That(rectTransform.anchorMax, Is.EqualTo(new Vector2(1f, 0.95f)));
    }

    [Test]
    public void SafeAreaIgnoresTransientDimensionsAndRecovers()
    {
        RectTransform rectTransform = CreateSafeArea();
        rectTransform.anchorMin = new Vector2(0.25f, 0.25f);
        rectTransform.anchorMax = new Vector2(0.75f, 0.75f);

        Assert.That(RefreshSafeArea(0, 2000, new Rect(0f, 0f, 1000f, 2000f)), Is.False);
        Assert.That(rectTransform.anchorMin, Is.EqualTo(new Vector2(0.25f, 0.25f)));
        Assert.That(rectTransform.anchorMax, Is.EqualTo(new Vector2(0.75f, 0.75f)));

        Assert.That(RefreshSafeArea(1000, 2000, new Rect(0f, 0f, 1000f, 2000f)), Is.True);
        Assert.That(rectTransform.anchorMin, Is.EqualTo(Vector2.zero));
        Assert.That(rectTransform.anchorMax, Is.EqualTo(Vector2.one));
    }

    [Test]
    public void CameraFramingUsesStableBaselineAcrossPortraitRatios()
    {
        Camera camera = CreateCameraAspectFitter();

        Assert.That(RefreshCamera(900, 2100), Is.True);
        Assert.That(camera.orthographicSize, Is.EqualTo(6.5625f).Within(0.0001f));

        Assert.That(RefreshCamera(750, 1000), Is.True);
        Assert.That(camera.orthographicSize, Is.EqualTo(5f).Within(0.0001f));

        Assert.That(RefreshCamera(900, 1600), Is.True);
        Assert.That(camera.orthographicSize, Is.EqualTo(5f).Within(0.0001f));

        Assert.That(RefreshCamera(900, 2100), Is.True);
        Assert.That(camera.orthographicSize, Is.EqualTo(6.5625f).Within(0.0001f));
    }

    [Test]
    public void CameraFramingSkipsUnchangedAspectAndRecoversFromTransientDimensions()
    {
        Camera camera = CreateCameraAspectFitter();

        Assert.That(RefreshCamera(900, 2100), Is.True);
        camera.orthographicSize = 42f;

        Assert.That(RefreshCamera(600, 1400), Is.False);
        Assert.That(camera.orthographicSize, Is.EqualTo(42f));
        Assert.That(RefreshCamera(900, 0), Is.False);
        Assert.That(float.IsNaN(camera.orthographicSize), Is.False);

        Assert.That(RefreshCamera(750, 1000), Is.True);
        Assert.That(camera.orthographicSize, Is.EqualTo(5f).Within(0.0001f));
    }

    private RectTransform CreateSafeArea()
    {
        testObject = new GameObject("Safe Area Test", typeof(RectTransform));
        Type safeAreaType = FindRuntimeType("SafeArea");
        testObject.AddComponent(safeAreaType);
        return testObject.GetComponent<RectTransform>();
    }

    private Camera CreateCameraAspectFitter()
    {
        testObject = new GameObject("Camera Aspect Test", typeof(Camera));
        Type fitterType = FindRuntimeType("CameraAspectFitter");
        Component fitter = testObject.AddComponent(fitterType);
        SetField(fitter, "desiredAspectRatio", 0.5625f);
        SetField(fitter, "baseOrthographicSize", 5f);
        return testObject.GetComponent<Camera>();
    }

    private bool RefreshSafeArea(int width, int height, Rect safeArea)
    {
        Component component = testObject.GetComponent(FindRuntimeType("SafeArea"));
        return InvokeRefresh(component, width, height, safeArea);
    }

    private bool RefreshCamera(int width, int height)
    {
        Component component = testObject.GetComponent(FindRuntimeType("CameraAspectFitter"));
        return InvokeRefresh(component, width, height);
    }

    private static bool InvokeRefresh(Component component, params object[] arguments)
    {
        MethodInfo method = component.GetType().GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(component, arguments);
    }

    private static void SetField(Component component, string fieldName, object value)
    {
        FieldInfo field = component.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(component, value);
    }

    private static Type FindRuntimeType(string typeName)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(typeName, false))
            .FirstOrDefault(candidate => candidate != null);
        Assert.That(type, Is.Not.Null);
        return type;
    }
}
