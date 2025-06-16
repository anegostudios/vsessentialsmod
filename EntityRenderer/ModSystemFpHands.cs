using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

#nullable disable

namespace Vintagestory.GameContent
{
    public class ModSystemFpHands : ModSystem
    {
        public IShaderProgram fpModeItemShader;
        public IShaderProgram fpModeHandShader;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;
        ICoreClientAPI capi;

        public override void StartClientSide(ICoreClientAPI capi)
        {
            this.capi = capi;
            capi.Event.ReloadShader += LoadShaders;
            LoadShaders();
        }

        public bool LoadShaders()
        {
            fpModeItemShader = createProg();
            capi.Shader.RegisterFileShaderProgram("standard", fpModeItemShader);

            fpModeHandShader = createProg();
            capi.Shader.RegisterFileShaderProgram("entityanimated", fpModeHandShader);

            bool ok = fpModeItemShader.Compile() && fpModeHandShader.Compile();
            if (ok)
            {
                foreach (var ubo in fpModeHandShader.UBOs.Values) ubo.Dispose();
                fpModeHandShader.UBOs.Clear();
                fpModeHandShader.UBOs["Animation"] = capi.Render.CreateUBO(fpModeHandShader, 0, "Animation", GlobalConstants.MaxAnimatedElements * 16 * 4);
            }

            return ok;
        }

        private IShaderProgram createProg()
        {
            var prog = capi.Shader.NewShaderProgram();
            prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            prog.VertexShader.PrefixCode = "#define ALLOWDEPTHOFFSET 1\r\n";
            prog.VertexShader.PrefixCode += "#define MAXANIMATEDELEMENTS " + GlobalConstants.MaxAnimatedElements + "\r\n";

            prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);
            prog.FragmentShader.PrefixCode = "#define ALLOWDEPTHOFFSET 1\r\n";
            return prog;
        }
    }






    
}
