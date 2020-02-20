using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;

public static class GalaxySim
{
    public struct BodyData
    {
        //public static BodyData FromBody(CelestialBody body)
        //{
        //    var res = new BodyData();
        //    res.x = body.position.x; res.y = body.position.y; res.z = body.position.z;
        //    res.vx = body.velocity.x; res.vy = body.velocity.y; res.vz = body.velocity.z;
        //    res.mass = body.mass;
        //    res.volume = body.volume;
        //    res.temp = body.temp;
        //    res.stat = 0;
        //    if (!body.exist) res.stat |= 1;
        //    if (body.kinetic) res.stat |= 2;
        //    return res;
        //}

        //public CelestialBody ToBody()
        //{
        //    if ((stat & 1) == 1) return null;
        //    return new CelestialBody(new Vector3(x, y, z), new Vector3(vx, vy, vz), mass, volume, temp, ((stat >> 1) & 1) == 1);
        //}

        //public void ToBody(ref CelestialBody body)
        //{
        //    if ((stat & 1) == 1)
        //    {
        //        body = null; return;
        //    }
        //}

        public Vector3 Position
        {
            get
            {
                return new Vector3(x, y, z);
            }
            set
            {
                x = value.x; y = value.y; z = value.z;
            }
        }
        public Vector3 Velocity
        {
            get
            {
                return new Vector3(vx, vy, vz);
            }
            set
            {
                vx = value.x; vy = value.y; vz = value.z;
            }
        }
        public float Volume => radius * radius * radius * 4f / 3f * Mathf.PI;
        public bool Exists
        {
            get
            {
                return (stat & 1) == 0;
            }
            set
            {
                if (value != Exists) stat ^= 1;
            }
        }
        public bool Kinetic
        {
            get
            {
                return ((stat >> 1) & 1) == 1;
            }
            set
            {
                if (value != Kinetic) stat ^= 2;
            }
        }
        public bool Neglectable
        {
            get
            {
                return ((stat >> 2) & 1) == 1;
            }
            set
            {
                if (value != Neglectable) stat ^= 1 << 2;
            }
        }

        public float x, y, z;
        public float vx, vy, vz;
        public float mass, radius, temp;
        public int collision;
        public int stat;
        // 00000000 00000000 00000000 00000000
        //                                  ||deleted
        //                                  |novelocity
        public BodyData(Vector3 position, Vector3 velocity, float mass, float radius, float temp = 0, bool neglectable = false, bool kinetic = false, bool exists = true)
        {
            x = position.x; y = position.y; z = position.z;
            vx = velocity.x; vy = velocity.y; vz = velocity.z;
            this.mass = mass;
            this.radius = radius;
            this.temp = temp;
            stat = 0;
            if (neglectable) stat |= 4;
            if (kinetic) stat |= 2;
            if (!exists) stat |= 1;
            collision = -1;
        }
        public override string ToString()
        {
            return string.Format("pos={0}\nvel={1}\nm={2};r={3};T={4};{5},{6}\n{7}", Position, Velocity, mass, radius, temp, Kinetic, Exists, System.Convert.ToString(stat, 2));
        }
    };
    const int DATA_LEN = 44;

    
    private static ComputeShader simShader;
    private static ComputeBuffer buffer1;
    private static ComputeBuffer buffer2;
    private static BodyData[] data;
    private static bool dataChanged = true;
    private static bool reversed;

    public static void SetupSimulation(BodyData[] bodies)
    {
        if (buffer1 != null) buffer1.Dispose();
        if (buffer2 != null) buffer2.Dispose();
        if (simShader == null ) simShader = Resources.Load<ComputeShader>("ComputeShaders/GalaxyCompute");
        buffer1 = new ComputeBuffer(bodies.Length, DATA_LEN);
        buffer2 = new ComputeBuffer(bodies.Length, DATA_LEN);
        reversed = false;
        data = new BodyData[bodies.Length];
        int dataIndex = 0;
        for (int i = 0; i < bodies.Length; i++)
        {
            if (!bodies[i].Neglectable)
                data[dataIndex++] = bodies[i];
        }
        int firstNeglectable = dataIndex;
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i].Neglectable)
                data[dataIndex++] = bodies[i];
        }
        buffer1.SetData(data);
        dataChanged = false;

        int kernel = simShader.FindKernel("Cal");
        simShader.SetBuffer(kernel, "Data1", buffer1);
        simShader.SetBuffer(kernel, "Data2", buffer2);
        simShader.SetInt("bodyCount", bodies.Length);
        simShader.SetInt("firstNeglectable", firstNeglectable);
    }

    public static void ModifySimulation(BodyData[] bodies)
    {
        (reversed ? buffer2 : buffer1).SetData(bodies);
        dataChanged = true;
    }

    public static void UpdateSimulation(float deltaTime, bool resetCollision = true)
    {

        int kernel = simShader.FindKernel("Cal");
        uint kernelX, kernelY, kernelZ;
        simShader.GetKernelThreadGroupSizes(kernel, out kernelX, out kernelY, out kernelZ);
        simShader.SetBool("reversed", reversed);
        simShader.SetBool("resetCollision", resetCollision);
        simShader.SetFloat("dt", deltaTime);
        simShader.Dispatch(kernel, buffer1.count / (int)kernelX + 1, 1, 1);

        dataChanged = true;
        reversed ^= true;
    }

    public static void UpdateCollision()
    {
        GetData(data);
        for (int i = 0; i < data.Length; i++)
        {
            BodyData me = data[i];
            if (!me.Exists || me.collision == -1) continue;
            BodyData kare = data[me.collision];
            if (!kare.Exists) continue;
            float rsum = me.radius + kare.radius;
            //if ((me.Position - kare.Position).sqrMagnitude > rsum * rsum) continue;
            //Debug.Log(string.Format("collision between {0} and {1}", i, me.collision));
            if (me.Neglectable)
            {
                if (kare.Neglectable)
                {
                    continue;
                }
                else
                {
                    Merge(ref data[me.collision], ref data[i]);
                }
            }
            else
            {
                if (kare.Neglectable)
                {
                    Merge(ref data[i], ref data[me.collision]);
                }
                else
                {
                    if (me.Kinetic)
                    {
                        if (kare.Kinetic)
                        {
                            continue;
                        }
                        else
                        {
                            Merge(ref data[i], ref data[me.collision]);
                        }
                    }
                    else
                    {
                        if (kare.Kinetic)
                        {
                            Merge(ref data[me.collision], ref data[i]);
                        }
                        else
                        {
                            Merge(ref data[i], ref data[me.collision]);
                            //Debug.Log(data[me.collision].ToString());
                        }
                    }
                }
            }
        }
        //Debug.Log(data.Count(d => d.Exists));
        ModifySimulation(data);
        dataChanged = false;
    }

    static void Merge(ref BodyData b1, ref BodyData b2)
    {
        BodyData res = b1;
        res.mass = b1.mass + b2.mass;
        res.vx = (b1.vx * b1.mass + b2.vx * b2.mass) / res.mass;
        res.vy = (b1.vy * b1.mass + b2.vy * b2.mass) / res.mass;
        res.vz = (b1.vz * b1.mass + b2.vz * b2.mass) / res.mass;
        res.x = (b1.x * b1.mass + b2.x * b2.mass) / res.mass;
        res.y = (b1.y * b1.mass + b2.y * b2.mass) / res.mass;
        res.z = (b1.z * b1.mass + b2.z * b2.mass) / res.mass;
        res.radius = Mathf.Pow((b1.radius * b1.radius * b1.radius) + (b2.radius * b2.radius * b2.radius), 1.0f / 3.0f);
        res.stat = b1.stat;
        b1 = res;
        b2.Exists = false;
        //res.temp = 
    }

    public static void GetData(BodyData[] buffer)
    {
        if (dataChanged)
        {
            (reversed ? buffer2 : buffer1).GetData(buffer);
            dataChanged = false;
        }
        else
        {
            Array.Copy(data, buffer, Math.Min(buffer.Length, data.Length));
        }
    }
}

//public class CelestialBody
//{
//    public Vector3 position;
//    public Vector3 velocity;
//    public float mass, volume, temp;
//    public bool exist, kinetic;

//    public CelestialBody(Vector3 position, Vector3 velocity, float mass, float volume, float temp = 0, bool kinetic = false)
//    {
//        this.position = position;
//        this.velocity = velocity;
//        this.mass = mass;
//        this.volume = volume;
//        this.temp = temp;
//        this.kinetic = kinetic;
//        this.exist = true;
//    }
//}