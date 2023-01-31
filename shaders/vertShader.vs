#version 330
layout(location = 0) in vec3 position;
  layout(location = 1) in vec2 vUV;
  uniform mat4 P;
  uniform mat4 V;
  out vec2 fUV;

  void main()
  {
    gl_Position = P * V * vec4(position, 1);
    fUV = vUV;
  }
