using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GalaxyBehaviour : MonoBehaviour
{
    GalaxySim.BodyData[] celestialBodies;
    public float timeStep; // seconds
    public int fixedStepCount; // steps in 1 FixedUpdate
    double timeElapsed = 0;
    bool useInstancing = true;

    float renderScale = 1 / 149597870700f;// when rendering, 1 Au = 1 meter
    float renderSizeScale = 1;
    public Mesh bodyMesh;
    public Mesh quad;
    public Material bodyMaterial;
    public Material hintMaterial;

    const float G = 6.67259e-11f;

    void Start()
    {
        bodyMaterial.enableInstancing = true;
        hintMaterial.enableInstancing = true;
        if (!SystemInfo.supportsComputeShaders) Debug.LogError("Your system doesn't support compute shaders");
        if (!SystemInfo.supportsInstancing)
        {
            useInstancing = false;
            Debug.LogWarning("Your system doesn't support instancing");
        }

        // add new celestial bodies into this List!
        List<GalaxySim.BodyData> bodiesList = new List<GalaxySim.BodyData>();
        var sun = new GalaxySim.BodyData(new Vector3(0, 0, 0), new Vector3(0, 0, 0), 1.9891e30f, 6.955e8f, 6000, false, true);
        var earth = new GalaxySim.BodyData(new Vector3(149597870700, 0, 0), new Vector3(0, 0, 29783), 5.965e24f * 1, 6371e3f, 300, false);
        var jupiter = new GalaxySim.BodyData(new Vector3(816520800000, 0, 0), new Vector3(0, 0, 13070), 1.9e27f * 1, 71492e3f, 300, false);
        bodiesList.Add(sun);
        bodiesList.Add(earth);
        bodiesList.Add(jupiter);
        //some random asteroids near jupiter orbit
        for (int i = 0; i < 1000; i++)
        {
            float randomRadius = UnityEngine.Random.value * Mathf.PI * 2;
            float randomAltitude = UnityEngine.Random.Range(0.9f * 816520800000, 1.2f * 816520800000);
            float orbitVel = Mathf.Sqrt(G * sun.mass / randomAltitude);
            float randomVelocity = UnityEngine.Random.Range(orbitVel * 0.8f, orbitVel * 1.0f);
            bodiesList.Add(new GalaxySim.BodyData(
                new Vector3(Mathf.Cos(randomRadius), 0, Mathf.Sin(randomRadius)) * randomAltitude + new Vector3(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value) * randomAltitude * 0.05f,
                new Vector3(-Mathf.Sin(randomRadius), 0, Mathf.Cos(randomRadius)) * randomVelocity * 1f + new Vector3(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value) * randomVelocity * 0.05f,
                100000, 600, 300, true));
        }

        celestialBodies = bodiesList.ToArray();
        GalaxySim.SetupSimulation(celestialBodies);
    }
    
    void Update()
    {
        GalaxySim.GetData(celestialBodies);
        RenderBodies();

        if (Input.GetKeyDown(KeyCode.T))
        {
            foreach (var body in celestialBodies)
            {
                Debug.Log(body.ToString());
            }
        }
        if (Input.GetKeyDown(KeyCode.S))
        {
            IOUtil.SaveToFile(System.IO.Path.Combine(System.Environment.CurrentDirectory, "savedBodies.bin"), celestialBodies);
        }
        if (Input.GetKeyDown(KeyCode.L))
        {
            var objArray = IOUtil.LoadFromFile(System.IO.Path.Combine(System.Environment.CurrentDirectory, "savedBodies.bin"), typeof(GalaxySim.BodyData));
            celestialBodies = new GalaxySim.BodyData[objArray.Length];
            for (int i = 0; i < objArray.Length; i++)
            {
                celestialBodies[i] = (GalaxySim.BodyData)objArray[i];
            }
            GalaxySim.SetupSimulation(celestialBodies);
            timeElapsed = 0;
        }
    }

    void FixedUpdate()
    {
        for (int i = 0; i < (fixedStepCount == 0 ? 10 : fixedStepCount); i++)
        {
            GalaxySim.UpdateSimulation((timeStep == 0 ? 86400 : timeStep), i == 0);
            timeElapsed += (timeStep == 0 ? 86400 : timeStep);
        }
        GalaxySim.UpdateCollision();
        //GalaxySim.GetData(celestialBodies);
    }

    void OnGUI()
    {
        GUI.Label(new Rect(0, 0, 150, 30), 
            string.Format("{0:0.00} years, {1} ", (timeElapsed / 86400 / 365), celestialBodies.Count(b => b.Exists))
            );
    }

    List<Matrix4x4> Lod0ToDraw = new List<Matrix4x4>();
    List<Matrix4x4> Lod1ToDraw = new List<Matrix4x4>();
    List<Tuple<Matrix4x4, Material>> SingleDraw = new List<Tuple<Matrix4x4, Material>>();
    void RenderBodies()
    {
        if (celestialBodies != null)
        {
            Lod0ToDraw.Clear();
            Lod1ToDraw.Clear();
            SingleDraw.Clear();
            var mainCam = Camera.main;
            var mainCamUp = mainCam.transform.up;
            var mainCamPos = mainCam.transform.position;
            foreach (var body in celestialBodies)
            {
                if (!body.Exists) continue;
                float renderDiameter = body.radius * 2 * renderScale * renderSizeScale;
                Vector3 renderPosition = body.Position * renderScale;
                Vector3 camDPos = renderPosition - mainCamPos;
                Matrix4x4 matrix;
                Material mat;
                float screenSize = renderDiameter * Screen.height /
                    (Vector3.Dot(mainCam.transform.forward, camDPos) * Mathf.Sin(mainCam.fieldOfView * Mathf.Deg2Rad) * 2);
                if (screenSize < 1.2f) renderDiameter /= screenSize;

                if (SanityCheck(renderPosition, renderDiameter))
                {
                    if (screenSize < 2)
                    {
                        matrix = Matrix4x4.TRS(renderPosition, Quaternion.LookRotation(camDPos, mainCamUp), Vector3.one * renderDiameter);
                        mat = SpecialRender(body);
                        if (mat != null)
                            SingleDraw.Add(new Tuple<Matrix4x4, Material>(matrix, mat));
                        else
                            Lod0ToDraw.Add(matrix);
                    }
                    else
                    {
                        matrix = Matrix4x4.TRS(renderPosition, Quaternion.identity, Vector3.one * renderDiameter);
                        mat = SpecialRender(body);
                        if (mat != null)
                            SingleDraw.Add(new Tuple<Matrix4x4, Material>(matrix, mat));
                        else
                            Lod1ToDraw.Add(matrix);
                    }
                }
            }
            //Debug.LogWarning("Draw" + (Lod0ToDraw.Count + Lod1ToDraw.Count) + "=" + Lod0ToDraw.Count + "+" + Lod1ToDraw.Count);
            if (useInstancing)
            {
                while (Lod0ToDraw.Count > 1023)
                {
                    Graphics.DrawMeshInstanced(quad, 0, bodyMaterial, Lod0ToDraw.GetRange(0, 1023));
                    Lod0ToDraw.RemoveRange(0, 1023);
                }
                Graphics.DrawMeshInstanced(quad, 0, bodyMaterial, Lod0ToDraw);
                while (Lod1ToDraw.Count > 1023)
                {
                    Graphics.DrawMeshInstanced(bodyMesh, 0, bodyMaterial, Lod1ToDraw.GetRange(0, 1023));
                    Lod1ToDraw.RemoveRange(0, 1023);
                }
                Graphics.DrawMeshInstanced(bodyMesh, 0, bodyMaterial, Lod1ToDraw);
            }
            else
            {
                foreach (var m in Lod0ToDraw)
                {
                    Graphics.DrawMesh(quad, m, bodyMaterial, 0);
                }
                foreach (var m in Lod1ToDraw)
                {
                    Graphics.DrawMesh(bodyMesh, m, bodyMaterial, 0);
                }
            }
            foreach (var pair in SingleDraw)
            {
                Graphics.DrawMesh(bodyMesh, pair.Item1, pair.Item2, 0);
            }
        }
    }

    Material SpecialRender(GalaxySim.BodyData body)
    {
        if (body.mass > 5e24f) return hintMaterial;
        return null;
    }

    bool SanityCheck(Vector3 renderPos, float renderScale)
    {
        return Mathf.Abs(renderPos.x) < 1e3f && Mathf.Abs(renderPos.y) < 1e3f && Mathf.Abs(renderPos.z) < 1e3f
            && renderScale < 1e2f;
    }

}
