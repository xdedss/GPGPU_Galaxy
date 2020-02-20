using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;

public static class GalaxySim
{
    public struct BodyData
    {

        /// <summary>
        /// (meters)
        /// </summary>
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
        /// <summary>
        /// (m/s)
        /// </summary>
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
        /// <summary>
        /// (m^3)
        /// </summary>
        public float Volume => radius * radius * radius * 4f / 3f * Mathf.PI;
        /// <summary>
        /// if set to false, this celestial body will be ignored
        /// </summary>
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
        /// <summary>
        /// if set to true, this celestial body will not reveive any force
        /// </summary>
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
        /// <summary>
        /// if set to true, this celestial body will only receive force and won't apply force to other bodies. Decreases complexity to O(n)
        /// </summary>
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
        /// <summary>
        /// Create a celestial body.
        /// </summary>
        /// <param name="position">position (meters)</param>
        /// <param name="velocity">velocity (m/s)</param>
        /// <param name="mass">mass (kilograms)</param>
        /// <param name="radius">radius (meters)</param>
        /// <param name="temp">temperature (K)</param>
        /// <param name="neglectable">if set to true, this celestial body will only receive force and won't apply force to other bodies. Decreases complexity to O(n)</param>
        /// <param name="kinetic">if set to true, this celestial body will not reveive any force</param>
        /// <param name="exists">if set to false, this celestial body will be ignored</param>
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
    public static int Count => data == null ? 0 : data.Length;

    /// <summary>
    /// Setup the simulation using an array of celestial bodies
    /// </summary>
    /// <param name="bodies"></param>
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

    /// <summary>
    /// Modify the simulation with new data. 
    /// </summary>
    /// <param name="bodies"></param>
    public static void ModifySimulation(BodyData[] bodies)
    {
        (reversed ? buffer2 : buffer1).SetData(bodies);
        dataChanged = true;
    }

    /// <summary>
    /// Update 1 step
    /// </summary>
    /// <param name="deltaTime">(seconds)</param>
    /// <param name="resetCollision">reset collision buffers</param>
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

    /// <summary>
    /// Merge collided celestial bodies according to collision buffers
    /// </summary>
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
                        }
                    }
                }
            }
        }
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
        //todo: calculate temperature
        //res.temp = 
        res.stat = b1.stat;
        b1 = res;
        b2.Exists = false;
    }

    /// <summary>
    /// Get data into buffer
    /// </summary>
    /// <param name="buffer"></param>
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