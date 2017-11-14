using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

public class MonoCraft : Game
{
   public MonoCraft(){ new GraphicsDeviceManager(this); }

   private BasicEffect basicEffect;
   private Texture2D atlas;

   private VertexBuffer vb;
   private int visibleFaces;

   private Vector3 playerPosition;
   private Vector2 playerCamAngles;
   private float playerYaccel;
   private VoxPos? aimedVoxel;
   private VoxPos? aimedBuildVoxel; //voxel on direction of aimed voxel surface

   ButtonState lastLMB = ButtonState.Released;
   ButtonState lastRMB = ButtonState.Released;

   private Vector3 DBGHIT;

   const int WORLD_DIM = 64;
   const int WORLD_VOL = WORLD_DIM*WORLD_DIM*WORLD_DIM;

   private const float GRAVITY = 9.8f; // meters per second per second

   private readonly byte[,,] data = new byte[WORLD_DIM, WORLD_DIM, WORLD_DIM];

   protected override void LoadContent()
   {
      atlas = Texture2D.FromStream(GraphicsDevice, new FileStream("art/tiles.png", FileMode.Open));

      playerPosition = new Vector3(WORLD_DIM/2, WORLD_DIM/2, WORLD_DIM/2);

      basicEffect = new BasicEffect(GraphicsDevice) {World = Matrix.Identity};

      vb = new VertexBuffer(GraphicsDevice, VertexPositionNormalTexture.VertexDeclaration, WORLD_VOL*3*4, BufferUsage.WriteOnly);
      
      var r = new Random();

      for (int z = 0; z < WORLD_DIM; z++) for (int y = 0; y < WORLD_DIM; y++) for (int x = 0; x < WORLD_DIM; x++)
            {
               byte id = (byte)(y < 30 ? r.Next(11,13) : ((y < 31 && r.Next(100) < 3) ? 54 : 0));
               data[x, y, z] = id;
            }

      BuildVB();
   }

   Rectangle TextureRectangleFromId(byte id)
   {
      const int TILE_SIZE = 128;
      int x = id%7; int y = id/7;
      return new Rectangle(x*TILE_SIZE+x*2,y*TILE_SIZE+y*2,TILE_SIZE,TILE_SIZE);
   }
   
   bool IsOpaqueSafe(VoxPos pos)
   {
      if(pos.IsValid())
         return data[pos.X, pos.Y, pos.Z] > 0;
      return false;
   }

   bool IsOpaqueSafe(int x, int y, int z)
   {
      return IsOpaqueSafe(new VoxPos(x,y,z));
   }

   byte? GetDataAtSafe(VoxPos pos)
   {
      if (pos.IsValid())
         return data[pos.X, pos.Y, pos.Z];
      return null;
   }

   private void BuildVB()
   {
      List<VertexPositionNormalTexture> verts = new List<VertexPositionNormalTexture>(WORLD_VOL/32);
      for (int z = 0; z < WORLD_DIM; z++) for (int y = 0; y < WORLD_DIM; y++) for (int x = 0; x < WORLD_DIM; x++)
            {
               byte voxel = data[x, y, z];
               if (voxel > 0)
               {
                  BuildVoxelGeometry(verts, x, y, z, TextureRectangleFromId(voxel),
                     !IsOpaqueSafe(x, y, z-1), !IsOpaqueSafe(x, y, z+1), !IsOpaqueSafe(x-1, y, z),
                     !IsOpaqueSafe(x+1, y, z), !IsOpaqueSafe(x, y+1, z), !IsOpaqueSafe(x, y-1, z));
               }
            }
      if(verts.Count > 0)
         vb.SetData(verts.ToArray());
   }

   public struct VoxPos
   {
      public int X, Y, Z;
      public VoxPos(int x, int y, int z) {X = x; Y = y; Z = z; } // kotlin where are you? :trollface:
      public VoxPos(float x, float y, float z) {X = (int) x; Y = (int) y; Z = (int) z; } //todo don't just cast
      public VoxPos(Vector3 v3) : this(v3.X, v3.Y, v3.Z) {}
      
      public bool IsValid()
      {
         if (X < 0 || Y < 0 || Z < 0 || X >= WORLD_DIM || Y >= WORLD_DIM || Z >= WORLD_DIM)
            return false;
         return true;
      }

      public Vector3 ToVector3()
      {
         return new Vector3(X,Y,Z);
      }

   }

   public struct VoxCol
   {
      public VoxPos pos;
      public Vector3 incident;
      public VoxCol(VoxPos pos, Vector3 incident) {this.pos = pos; this.incident = incident; }
   }

   private List<VoxCol> RaycastFromTo(Vector3 from, Vector3 to)
   {
      float lerpT = 0;
      List<VoxCol> voxels = new List<VoxCol>();

      for (float t = 0; t < 1; t+=0.0001f) //precision... this is sloooooooooooow
      {
         Vector3 curPos = Vector3.LerpPrecise(from, to, t);
         VoxPos curVox = new VoxPos(curPos.X, curPos.Y, curPos.Z);
         if(voxels.Count == 0 || !voxels[voxels.Count-1].pos.Equals(curVox))
            voxels.Add(new VoxCol(curVox, curPos));
      }

      Debug.WriteLine(voxels.Count);

      return voxels;
   }

   private VoxCol? GetFirstSolidSafe(List<VoxCol> list)
   {
      foreach (var voxCol in list)
      {
         int x = voxCol.pos.X; int y = voxCol.pos.Y; int z = voxCol.pos.Z;
         if (x < 0 || y < 0 || z < 0 || x >= WORLD_DIM || y >= WORLD_DIM || z >= WORLD_DIM)
            continue;
         if (data[x,y,z] > 0)
            return voxCol;
      }
      return null;
   }

   VoxPos GetBuildPos(VoxCol col)
   {
      Vector3 center = col.pos.ToVector3()+Vector3.One * 0.5f;
      Vector3 local = col.incident-center;

      float lX = Math.Abs(local.X);
      float lY = Math.Abs(local.Y);
      float lZ = Math.Abs(local.Z);

      VoxPos pos = col.pos;

      if (lY > lX)
         if (lZ > lY) // z is bigger
            pos.Z += local.Z > 0 ? 1 : -1;
         else // y is bigger
            pos.Y += local.Y > 0 ? 1 : -1;
      else if (lZ > lX) // z is bigger
         pos.Z += local.Z > 0 ? 1 : -1;
      else // x is bigger
         pos.X += local.X > 0 ? 1 : -1;

      return pos;
   }

   //public enum Face : byte {Nzp,Szn,Exp,Wxn,Uyp,Dyn}

   void BuildVoxelGeometry(List<VertexPositionNormalTexture> listToAppend, int x, int y, int z, Rectangle textRect, bool n, bool s, bool w, bool e, bool u, bool d)
   {
      if (n) listToAppend.AddRange(BuildFace(new Vector3(x,y,z),    new Vector3(x,y+1,z),    new Vector3(x+1,y+1,z),  new Vector3(x+1,y,z),  new Vector3(0,0,1), textRect)); //south
      if (s) listToAppend.AddRange(BuildFace(new Vector3(x+1,y,z+1),new Vector3(x+1,y+1,z+1),new Vector3(x,y+1,z+1),  new Vector3(x,y,z+1),  new Vector3(0,0,-1),textRect)); //north
      if (w) listToAppend.AddRange(BuildFace(new Vector3(x,y,z+1),  new Vector3(x,y+1,z+1),  new Vector3(x,y+1,z),    new Vector3(x,y,z),    new Vector3(-1,0,0),textRect)); //west
      if (e) listToAppend.AddRange(BuildFace(new Vector3(x+1,y,z),  new Vector3(x+1,y+1,z),  new Vector3(x+1,y+1,z+1),new Vector3(x+1,y,z+1),new Vector3(1,0,0), textRect)); //east
      if (u) listToAppend.AddRange(BuildFace(new Vector3(x,y+1,z),  new Vector3(x,y+1,z+1),  new Vector3(x+1,y+1,z+1),new Vector3(x+1,y+1,z),new Vector3(0,1,0), textRect)); //top
      if (d) listToAppend.AddRange(BuildFace(new Vector3(x+1,y,z),  new Vector3(x+1,y,z+1),  new Vector3(x,y,z+1),    new Vector3(x,y,z),    new Vector3(0,-1,0),textRect)); //bottom
   }
      
   VertexPositionNormalTexture[] BuildFace(Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, Vector3 normal, Rectangle textureRectangle)
   {
      VertexPositionNormalTexture[] faceGeom = new VertexPositionNormalTexture[6];
      Vector2 uvMin = new Vector2(textureRectangle.X/(float)atlas.Width, textureRectangle.Bottom/(float)atlas.Height);
      Vector2 uvMax = new Vector2(textureRectangle.Right/(float)atlas.Width, textureRectangle.Y/(float)atlas.Height);
      faceGeom[0] = new VertexPositionNormalTexture(bl, normal, uvMin);
      faceGeom[1] = new VertexPositionNormalTexture(tl, normal, new Vector2(uvMin.X, uvMax.Y));
      faceGeom[2] = new VertexPositionNormalTexture(tr, normal, uvMax);
      faceGeom[3] = new VertexPositionNormalTexture(bl, normal, uvMin);
      faceGeom[4] = new VertexPositionNormalTexture(tr, normal, uvMax);
      faceGeom[5] = new VertexPositionNormalTexture(br, normal, new Vector2(uvMax.X, uvMin.Y));
      visibleFaces++;
      return faceGeom;
   }

   protected override void Update(GameTime gameTime)
   {
      KeyboardState keyboardState = Keyboard.GetState();

      if (keyboardState.IsKeyDown(Keys.Escape)) Exit();

      float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

      VoxPos playerVosPos = new VoxPos(playerPosition);

      // player gravity
      playerYaccel -= GRAVITY*deltaTime;
      playerPosition.Y += playerYaccel*deltaTime;
      if (IsOpaqueSafe(playerVosPos))
      {
         if (keyboardState.GetPressedKeys().Contains(Keys.Space))
         {
            playerYaccel = GRAVITY/2; // ???
         }
         else
         {
            playerPosition.Y = playerVosPos.Y+1;
            playerYaccel = 0;
         }
      }

      // player movement
      Vector2 movementInput = new Vector2(keyboardState.IsKeyDown(Keys.A) ? 1 : (keyboardState.IsKeyDown(Keys.D) ? -1 : 0),
                                          keyboardState.IsKeyDown(Keys.W) ? 1 : (keyboardState.IsKeyDown(Keys.S) ? -1 : 0));
      if (movementInput.Length() > 1) movementInput.Normalize();
      if (keyboardState.IsKeyDown(Keys.LeftShift)) movementInput *= 3;
      playerPosition += new Vector3(
         (float)Math.Cos(playerCamAngles.Y-MathHelper.PiOver2)*movementInput.Y-(float)Math.Cos(playerCamAngles.Y)*movementInput.X,0,
         (float)Math.Sin(playerCamAngles.Y-MathHelper.PiOver2)*movementInput.Y-(float)Math.Sin(playerCamAngles.Y)*movementInput.X
         )*(float)gameTime.ElapsedGameTime.TotalSeconds*3;


      //camera controls
      Point windowCenter = new Point(GraphicsDevice.Viewport.Width/2,GraphicsDevice.Viewport.Height/2);
      Point mouseDelta = Mouse.GetState().Position-windowCenter;
      Mouse.SetPosition(windowCenter.X, windowCenter.Y);
      
      playerCamAngles += new Vector2(mouseDelta.Y, mouseDelta.X)*(float)gameTime.ElapsedGameTime.TotalSeconds*0.5f;
      playerCamAngles.X = MathHelper.Clamp(playerCamAngles.X, -MathHelper.PiOver2, MathHelper.PiOver2);


      // get aiming voxel
      Vector3 aimEndLocal = Vector3.UnitZ*5;
      Vector3 aimEndRotLocal = Vector3.Transform(aimEndLocal, Matrix.CreateRotationX(playerCamAngles.X)*Matrix.CreateRotationY(-playerCamAngles.Y+MathHelper.Pi));
      Vector3 aimEndGlobal = playerPosition+aimEndRotLocal;

      Vector3 playerCamPosition = playerPosition+Vector3.UnitY;
      var voxelsTraversed = RaycastFromTo(playerCamPosition, aimEndGlobal);

      ButtonState curLMB = Mouse.GetState().LeftButton;
      ButtonState curRMB = Mouse.GetState().RightButton;

      VoxCol? currentlyAiming = GetFirstSolidSafe(voxelsTraversed);

      if (currentlyAiming != null)
      {
         VoxPos aimed = ((VoxCol)currentlyAiming).pos;
         aimedVoxel = aimed;

         if (aimed.IsValid())
         {
            VoxPos buildPos = GetBuildPos((VoxCol) currentlyAiming);
            aimedBuildVoxel = buildPos;
            if (curLMB == ButtonState.Pressed && lastLMB == ButtonState.Released)
            {
               data[aimed.X, aimed.Y, aimed.Z] = 0;
               BuildVB();
            }
            else if (buildPos.IsValid() && curRMB == ButtonState.Pressed && lastRMB == ButtonState.Released)
            {
               if (data[buildPos.X, buildPos.Y, buildPos.Z] == 0) //raycast isn't 100% accurate, so we gotta check this
               {
                  data[buildPos.X, buildPos.Y, buildPos.Z] = 4;
                  BuildVB();
               }
            }
         }

         DBGHIT = ((VoxCol) currentlyAiming).incident;

      }
      else
      {
         aimedVoxel = null;
         aimedBuildVoxel = null;
      }

      lastLMB = curLMB;
      lastRMB = curRMB;

      base.Update(gameTime);
   }

   protected override void Draw(GameTime gameTime)
   {
      GraphicsDevice.Clear(Color.CornflowerBlue);
      
      basicEffect.View = Matrix.CreateTranslation(-playerPosition - Vector3.UnitY);
      basicEffect.Projection = Matrix.CreateRotationY(playerCamAngles.Y)*Matrix.CreateRotationX(playerCamAngles.X)*Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(80),16/9f,0.01f,100f);
      basicEffect.VertexColorEnabled = false;
      basicEffect.TextureEnabled = true;
      basicEffect.Texture = atlas;
      basicEffect.EnableDefaultLighting();
 
      GraphicsDevice.SetVertexBuffer(vb);
      GraphicsDevice.RasterizerState = new RasterizerState {CullMode = CullMode.CullClockwiseFace};;
      GraphicsDevice.BlendState = BlendState.Opaque;
      GraphicsDevice.DepthStencilState = DepthStencilState.Default;
 
      foreach (EffectPass pass in basicEffect.CurrentTechnique.Passes)
      {
         pass.Apply();
         GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, visibleFaces*2);
      }
      

      //draw indication of voxel we're aiming at
      
      //GraphicsDevice.RasterizerState = new RasterizerState {CullMode = CullMode.None};
      GraphicsDevice.BlendState = BlendState.NonPremultiplied;
      GraphicsDevice.DepthStencilState = DepthStencilState.None;
      
      if (aimedVoxel != null)
      {
         List<VertexPositionNormalTexture> indList = new List<VertexPositionNormalTexture>();

         VoxPos cast = (VoxPos) aimedVoxel;
         
         BuildVoxelGeometry(indList, cast.X, cast.Y, cast.Z, TextureRectangleFromId(6), true, true, true, true, true, true);

         if (aimedBuildVoxel != null)
         {
            VoxPos bcast = (VoxPos) aimedBuildVoxel;
            BuildVoxelGeometry(indList, bcast.X, bcast.Y, bcast.Z, TextureRectangleFromId(13), true, true, true, true, true, true);
         }

         Vector2 someTranspPixel = new Vector2(550/1024f, 1330/2048f);
         Matrix rotationY = Matrix.CreateRotationY(-playerCamAngles.Y);
         indList.Add(new VertexPositionNormalTexture(DBGHIT,Vector3.One, someTranspPixel));
         indList.Add(new VertexPositionNormalTexture(DBGHIT+(Vector3.UnitY+Vector3.Transform(Vector3.UnitX, rotationY))*0.2f,Vector3.One, someTranspPixel));
         indList.Add(new VertexPositionNormalTexture(DBGHIT+(Vector3.UnitY-Vector3.Transform(Vector3.UnitX, rotationY))*0.2f,Vector3.One, someTranspPixel));
         
         VertexBuffer indicatorVb = new VertexBuffer(GraphicsDevice, VertexPositionNormalTexture.VertexDeclaration,
            indList.Count+3, BufferUsage.WriteOnly);
         indicatorVb.SetData(indList.ToArray());
         GraphicsDevice.SetVertexBuffer(indicatorVb);

         GraphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, indList.Count / 3+1);
      }
      
      base.Draw(gameTime);
   }

}
