#version 330 
 in vec2 fUV;
  out vec4 color;
  uniform sampler2D tex;

  void main()
  {
    color = texture(tex, fUV);
  }