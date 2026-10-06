using System;
using Silk.NET.OpenGL;
using Silk.NET.SDL;

namespace Emulator80386.App.GFX
{
    public unsafe class GlDisplayWindow : IDisposable
    {
        private Sdl? _sdl;
        private GL? _gl;
        private Window* _window = null;
        private void* _glContext = null;
        private bool _initialized = false;

        private uint _vao;
        private uint _vbo;
        private uint _texture;
        private uint _shaderProgram;

        public bool IsInitialized => _initialized;

        public GlDisplayWindow(string title, int width, int height)
        {
            try
            {
                _sdl = Sdl.GetApi();
                if (_sdl.Init(Sdl.InitVideo) < 0)
                {
                    Console.WriteLine("SDL Init Video failed (headless mode).");
                    return;
                }

                _window = _sdl.CreateWindow(
                    title,
                    Sdl.WindowposCentered,
                    Sdl.WindowposCentered,
                    width,
                    height,
                    (uint)(WindowFlags.Opengl | WindowFlags.Shown | WindowFlags.Resizable)
                );

                if (_window == null)
                {
                    Console.WriteLine("SDL Window creation failed (headless mode).");
                    return;
                }

                _glContext = _sdl.GLCreateContext(_window);
                if (_glContext == null)
                {
                    Console.WriteLine("OpenGL Context creation failed.");
                    return;
                }

                _gl = GL.GetApi(proc => (nint)_sdl.GLGetProcAddress(proc));

                InitGlResources();
                _initialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GlDisplayWindow initialization error (headless fallback enabled): {ex.Message}");
                _initialized = false;
            }
        }

        private void InitGlResources()
        {
            if (_gl == null) return;

            string vertexShaderSource = @"
                #version 330 core
                layout (location = 0) in vec2 aPos;
                layout (location = 1) in vec2 aTexCoord;
                out vec2 TexCoord;
                void main() {
                    gl_Position = vec4(aPos, 0.0, 1.0);
                    TexCoord = aTexCoord;
                }";

            string fragmentShaderSource = @"
                #version 330 core
                out vec4 FragColor;
                in vec2 TexCoord;
                uniform sampler2D uTexture;
                void main() {
                    FragColor = texture(uTexture, TexCoord);
                }";

            uint vertShader = _gl.CreateShader(ShaderType.VertexShader);
            _gl.ShaderSource(vertShader, vertexShaderSource);
            _gl.CompileShader(vertShader);

            uint fragShader = _gl.CreateShader(ShaderType.FragmentShader);
            _gl.ShaderSource(fragShader, fragmentShaderSource);
            _gl.CompileShader(fragShader);

            _shaderProgram = _gl.CreateProgram();
            _gl.AttachShader(_shaderProgram, vertShader);
            _gl.AttachShader(_shaderProgram, fragShader);
            _gl.LinkProgram(_shaderProgram);

            _gl.DeleteShader(vertShader);
            _gl.DeleteShader(fragShader);

            // Fullscreen Quad: Pos(x, y), TexCoord(u, v)
            float[] quadVertices = new float[]
            {
                // Pos        // Tex
                -1.0f,  1.0f,  0.0f, 0.0f,
                -1.0f, -1.0f,  0.0f, 1.0f,
                 1.0f, -1.0f,  1.0f, 1.0f,

                -1.0f,  1.0f,  0.0f, 0.0f,
                 1.0f, -1.0f,  1.0f, 1.0f,
                 1.0f,  1.0f,  1.0f, 0.0f
            };

            _vao = _gl.GenVertexArray();
            _vbo = _gl.GenBuffer();

            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

            fixed (float* v = quadVertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quadVertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            }

            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);

            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);

            // Create Texture
            _texture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _texture);

            int filterMode = (int)GLEnum.Nearest;
            int wrapMode = (int)GLEnum.ClampToEdge;

            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, in filterMode);
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, in filterMode);
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, in wrapMode);
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, in wrapMode);

            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 640, 400, 0, Silk.NET.OpenGL.PixelFormat.Rgba, Silk.NET.OpenGL.PixelType.UnsignedByte, null);
        }

        public void RenderFrameBuffer(uint[] pixelBuffer, int width = 640, int height = 400)
        {
            if (!_initialized || _gl == null || pixelBuffer == null) return;

            _gl.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit);

            _gl.BindTexture(TextureTarget.Texture2D, _texture);
            fixed (uint* ptr = pixelBuffer)
            {
                _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, (uint)width, (uint)height, Silk.NET.OpenGL.PixelFormat.Rgba, Silk.NET.OpenGL.PixelType.UnsignedByte, ptr);
            }

            _gl.UseProgram(_shaderProgram);
            _gl.BindVertexArray(_vao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        }

        public bool PollEvents(Action<byte>? onScancode)
        {
            if (!_initialized || _sdl == null) return true;

            Event ev;
            while (_sdl.PollEvent(&ev) != 0)
            {
                if (ev.Type == (uint)EventType.Quit)
                {
                    return false;
                }
                else if (ev.Type == (uint)EventType.Keydown)
                {
                    byte scancode = (byte)((int)ev.Key.Keysym.Scancode & 0xFF);
                    onScancode?.Invoke(scancode);
                }
            }
            return true;
        }

        public void SwapBuffers()
        {
            if (_initialized && _sdl != null && _window != null)
            {
                _sdl.GLSwapWindow(_window);
            }
        }

        public void Dispose()
        {
            if (_initialized)
            {
                if (_gl != null)
                {
                    _gl.DeleteVertexArray(_vao);
                    _gl.DeleteBuffer(_vbo);
                    _gl.DeleteTexture(_texture);
                    _gl.DeleteProgram(_shaderProgram);
                }

                if (_sdl != null)
                {
                    if (_glContext != null) _sdl.GLDeleteContext(_glContext);
                    if (_window != null) _sdl.DestroyWindow(_window);
                    _sdl.Quit();
                }
                _initialized = false;
            }
        }
    }
}
