using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using Skybox;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace Skybox
{
    public partial class Form1 : Form
    {
        // Constants

        int makeShader(string code, ShaderType shaderType)
        {
            int shader = GL.CreateShader(shaderType);
            GL.ShaderSource(shader, code);
            GL.CompileShader(shader);

            //GL.Assert(shader, GL_COMPILE_STATUS, glGetShaderiv, glGetShaderInfoLog);

            return shader;
        }
        public struct gamestate
        {
            public float x, y, z, r, r2;
            public double px, py;
        }

        public class scene
        {
            public List<entity> entities = new List<entity>();
            public gamestate state;
            public int entity_count => entities.Count;
        }
        scene makeScene()
        {
            scene ret = new scene();
            return ret;
        }

        public class entity
        {
            public int buffer;
            public int vertices;
            public int vao;
            public int depth_test;
            public int texcount;
            public int program;
            public int P;
            public int V;
            public int M;
            public int tex;
            public int time;
            public int? fb;
            public int[] textures;
        }

        public static string ReadResourceTxt(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fr1 = assembly.GetManifestResourceNames().First(z => z.Contains(resourceName));

            using (Stream stream = assembly.GetManifestResourceStream(fr1))
            using (StreamReader reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        int blankTexture(int w, int h, PixelInternalFormat format, OpenTK.Graphics.OpenGL.PixelFormat format2)
        {
            int texture;
            GL.GenTextures(1, out texture);
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, format, w, h, 0, format2, PixelType.Float, (IntPtr)0);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)All.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)All.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)All.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)All.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareFunc, (int)All.Lequal);
            return texture;
        }
        int makeFramebuffer(entity e, int w, int h)
        {
            var renderTexture = blankTexture(w, h, PixelInternalFormat.Rgba, OpenTK.Graphics.OpenGL.PixelFormat.Bgra);
            var depthTexture = blankTexture(w, h, PixelInternalFormat.DepthComponent, OpenTK.Graphics.OpenGL.PixelFormat.DepthComponent);
            e.textures[0] = renderTexture;
            e.textures[1] = depthTexture;

            int framebuffer;

            GL.GenFramebuffers(1, out framebuffer);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, renderTexture, 0);
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, depthTexture, 0);
            GL.DrawBuffers(2, new DrawBuffersEnum[] { DrawBuffersEnum.ColorAttachment0,
                (DrawBuffersEnum)All.DepthAttachment
                /*GL_DEPTH_ATTACHMENT*/ });
            return framebuffer;
        }

        int makeBuffer(BufferTarget target, long size, float[] data)
        {
            int buffer;
            GL.GenBuffers(1, out buffer);
            GL.BindBuffer(target, buffer);
            GL.BufferData(target, (int)size, data, BufferUsageHint.StaticDraw);
            return buffer;
        }


        entity makeEntity(scene s, string vs, string fs, int texcount, string[] textures, float[] data, int vertices, int layouts, int is_framebuffer,

            int w, int h)
        {
            entity e = new entity() { texcount = texcount, vertices = vertices };
            s.entities.Add(e);

            //entity e = { .vertices = vertices, .texcount = texcount, .depth_test = depth_test };

            // Create VAO
            GL.GenVertexArrays(1, out e.vao);
            GL.BindVertexArray(e.vao);

            // Create Buffer
            e.buffer = makeBuffer(BufferTarget.ArrayBuffer, sizeof(float) * vertices * layouts * 3, data);
            GL.BindBuffer(BufferTarget.ArrayBuffer, e.buffer);

            // Load Attribute Pointers
            for (int i = 0; i < layouts; i++)
            {
                GL.EnableVertexAttribArray(i);
                GL.VertexAttribPointer(i, 3, VertexAttribPointerType.Float, false, (int)(sizeof(float) * layouts * 3), (sizeof(float) * i * 3));
            }

            string vShaderCode = ReadResourceTxt(vs);
            string fShaderCode = ReadResourceTxt(fs);
            // Load Program
            e.program = makeProgram(vShaderCode, fShaderCode);
            e.P = GL.GetUniformLocation(e.program, "P");
            e.V = GL.GetUniformLocation(e.program, "V");
            e.M = GL.GetUniformLocation(e.program, "M");
            e.tex = GL.GetUniformLocation(e.program, "tex");
            e.time = GL.GetUniformLocation(e.program, "time");

            // Load Textures
            if (is_framebuffer == 0)
                for (int i = 0; i < texcount; i++)
                    if (textures[i][0] > 0)
                        e.textures[i] = loadTexture(textures[i]);

            // Create a framebuffer if applicable
            if (is_framebuffer == 1)
            {
                e.textures = new int[2];
                e.fb = makeFramebuffer(e, w, h);
            }

            //s->entities = realloc(s->entities, ++s->entity_count * sizeof(entity));
            //memcpy(&s->entities[s->entity_count - 1], &e, sizeof(entity));


            return e;
        }

        public class matrix
        {
            public float[] m = new float[16];
        }

        matrix getProjectionMatrix(int w, int h)
        {
            float fov = 65.0f;
            float aspect = (float)w / (float)h;
            float near = 1.0f;
            float far = 1000.0f;

            var ret = new matrix();
            ret.m[0] = (float)(1.0f / (aspect * Math.Tan(fov * 3.14f / 180.0f / 2.0f)));
            ret.m[5] = (float)(1.0f / Math.Tan(fov * 3.14f / 180.0f / 2.0f));
            ret.m[10] = -(far + near) / (far - near);
            ret.m[11] = -1.0f;
            ret.m[14] = -(2.0f * far * near) / (far - near);
            return ret;
        }

        matrix getViewMatrix(float x, float y, float z, float a, float p)
        {
            float cosy = (float)Math.Cos(a), siny = (float)Math.Sin(a), cosp = (float)Math.Cos(p), sinp = (float)Math.Sin(p);
            var ret = new matrix();

            ret.m[0] = cosy;
            ret.m[1] = siny * sinp;
            ret.m[2] = siny * cosp;
            ret.m[5] = cosp;
            ret.m[6] = -sinp;
            ret.m[8] = -siny;
            ret.m[9] = cosy * sinp;
            ret.m[10] = cosp * cosy;
            ret.m[12] = -(cosy * x - siny * z);
            ret.m[13] = -(siny * sinp * x + cosp * y + cosy * sinp * z);
            ret.m[14] = -(siny * cosp * x - sinp * y + cosp * cosy * z);
            ret.m[15] = 1.0f;

            return ret;
        }
        void renderScene(scene s, int w, int h, float time)
        {
            matrix p = getProjectionMatrix(w, h);
            matrix v = getViewMatrix(s.state.x, s.state.y, s.state.z, s.state.r, s.state.r2);
            for (int i = 0; i < s.entity_count; i++)
                if (s.entities[i].fb == null)
                    renderEntity(s.entities[i], p, v, time);
            for (int i = 0; i < s.entity_count; i++)
                if (s.entities[i].fb != null)
                    renderEntity(s.entities[i], p, v, 0.0f);
        }

        void renderEntity(entity e, matrix P, matrix V, float time)
        {
            GL.UseProgram(e.program);
            for (int i = 0; i < (e.fb != null ? 2 : e.texcount); i++)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + (int)i);
                GL.BindTexture(TextureTarget.Texture2D, e.textures[i]);
                GL.Uniform1(e.tex + i, i);
            }
            GL.UniformMatrix4(e.P, 1, false, P.m);
            GL.UniformMatrix4(e.V, 1, false, V.m);
            GL.Uniform1(e.time, time);

            if (e.fb != null)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            }

            if (e.depth_test == 0)
                GL.Disable(EnableCap.DepthTest);
            GL.BindVertexArray(e.vao);
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, (int)e.vertices);
            if (e.depth_test == 0)
                GL.Enable(EnableCap.DepthTest);
        }

        int makeProgram(string vertexShaderSource, string fragmentShaderSource)
        {
            int vertexShader = makeShader(vertexShaderSource, ShaderType.VertexShader);
            int fragmentShader = makeShader(fragmentShaderSource, ShaderType.FragmentShader);

            int program = GL.CreateProgram();
            //if (vertexShader)
            {
                GL.AttachShader(program, vertexShader);
            }
            //  if (fragmentShader)
            {
                GL.AttachShader(program, fragmentShader);
            }
            GL.LinkProgram(program);

            //glAssert(program, GL_LINK_STATUS, glGetProgramiv, glGetProgramInfoLog);
            /*
            if (vertexShader) { glDetachShader(program, vertexShader); }
            if (vertexShader) { glDeleteShader(vertexShader); }
            if (fragmentShader) { glDetachShader(program, fragmentShader); }
            if (fragmentShader) { glDeleteShader(fragmentShader); }*/

            return program;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            mf = new MessageFilter();
            Application.AddMessageFilter(mf);
        }

        MessageFilter mf = null;

        public Form1()
        {
            InitializeComponent();
            start = DateTime.Now;
            MouseWheel += Form1_MouseWheel;
            glControl = new OpenTK.GLControl(new OpenTK.Graphics.GraphicsMode(32, 24, 0, 4), 3, 3, OpenTK.Graphics.GraphicsContextFlags.Default);
            glControl.MouseMove += GlControl_MouseMove;
            glControl.Paint += Gl_Paint;
            Controls.Add(glControl);
            glControl.Dock = DockStyle.Fill;
            Width = SCR_WIDTH;
            Height = SCR_HEIGHT;
        }

        private void GlControl_MouseMove(object sender, MouseEventArgs e)
        {

        }

        private void Form1_MouseWheel(object sender, MouseEventArgs e)
        {

        }

        GLControl glControl;

        bool first = true;
        const int SCR_WIDTH = 800;
        const int SCR_HEIGHT = 600;

        scene s;
        void init()
        {
            s = makeScene();
            makeEntity(s, "skyVertShader", "skyFragShader", 0, null, null, 4, 0, 0, 0, 0);
            makeEntity(s, "postVertShader", "postFragShader", 0, null, null, 4, 0, 1, 800, 600);
        }


        private void Gl_Paint(object sender, PaintEventArgs e)
        {
            if (!glControl.Context.IsCurrent)
                glControl.MakeCurrent();

            GL.Viewport(0, 0, glControl.Width, glControl.Height);

            if (first)
            {

                init();
                GL.Enable(EnableCap.DepthTest);

                GL.DepthFunc(DepthFunction.Less);
                GL.Enable(EnableCap.CullFace);

                GL.ClearColor(0.0f, 0.0f, 0.0f, 1f);
                first = false;
            }

            Redraw();
            glControl.SwapBuffers();
        }

        private int loadTexture(string txt)
        {
            return 0;
        }

        DateTime start;
        void Redraw()
        {

            // Move Cursor
            double mx, my;
            var cur = Cursor.Position;
            mx = cur.X;
            my = cur.Y;
            s.state.r -= (float)(mx - s.state.px) * 0.01f;
            s.state.r2 -= (float)(my - s.state.py) * 0.01f;
            s.state.px = (float)mx;
            s.state.py = (float)my;

            // Clear Framebuffer
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, s.entities[1].fb.Value);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Render the Scene
            float time = (float)((DateTime.Now - start).TotalSeconds);
            time = (float)time * 2.2f - 0.0f;
            renderScene(s, 800, 600, time);
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            glControl.Invalidate();
        }
    }

}

