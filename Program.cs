using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
	partial class Program : MyGridProgram
	{

		#region mdk preserve
		//####################################################
		// Config

		// Name of camera block to designate target
		const string Camera = "Camera";

		// Echos what is printed in terminal
		//const string DisplayBlock = "LCD Panel";
		const string DisplayBlock = "Fighter Cockpit";

		// For blocks that have multiple displays like button panels, cockpit, or programmable blocks
		//const int DisplayIndex = 0;
		const int DisplayIndex = 3;

		// Antenna block
		const string Antenna = "Antenna";

		// Name of the radio channel to use.
		// This must match the artillery battery that you want to fire
		string RadioChannel = "smArty";

		// How long status messages will remain on screen / terminal
		// Deprecated by new display format
		int messageTimeout = 10; // seconds

		// If no transmissions are received within X seconds from
		// a certain gun, drop it from the display LCD.
		TimeSpan GunDisplayTimeout = TimeSpan.FromSeconds(10);

		//####################################################
		// Code below
		#endregion

		IMyCameraBlock cam;
		IMyGridTerminalSystem G;
		IMyRadioAntenna ant;
		//int linkCount = 0;
		StringBuilder printBuf = new StringBuilder();
		IMyTextSurface disp;

		public enum SmartyProgramState
		{
			Offline,
			Aiming,
			Stowing,
			Idle,
			Firing,
			OutOfRange
		};

		class GunStatus
		{
			public long id;
			public DateTime lastMessageTime;
			public SmartyProgramState state;
			public bool steady;
		};
		Dictionary<long, GunStatus> status = new Dictionary<long, GunStatus>();

		public Program()
		{
			G = GridTerminalSystem;
			cam = G.GetBlockWithName(Camera) as IMyCameraBlock;
			ant = G.GetBlockWithName(Antenna) as IMyRadioAntenna;
			if (!checkBlocks()) { return; }
			cam.EnableRaycast = true;
			Runtime.UpdateFrequency = UpdateFrequency.Update10;
			var db = G.GetBlockWithName(DisplayBlock);
			if (db == null)
			{
				Echo("(optional) Display block not found");
			}
			else
			{
				if (db is IMyTextSurface)
				{
					disp = db as IMyTextSurface;
					//disp.ContentType = ContentType.TEXT_AND_IMAGE;
					disp.ContentType = ContentType.SCRIPT;
					disp.Script = "";
				}
				else if (db is IMyTextSurfaceProvider)
				{
					var tsp = db as IMyTextSurfaceProvider;
					if (DisplayIndex >= 0 && DisplayIndex < tsp.SurfaceCount)
					{
						disp = tsp.GetSurface(DisplayIndex);
						//disp.ContentType = ContentType.TEXT_AND_IMAGE;
						disp.ContentType = ContentType.SCRIPT;
						disp.Script = "";
					}
					else
					{
						Print("Warning: DisplayIndex out of bounds");
					}
				}
			}
		}

		public void Save()
		{
		}

		public void Main(string arg, UpdateType updateSource)
		{
			//IGC.SendBroadcastMessage("smArty", "orient");
			printBuf.Clear();

			Print("Listening " + Spinner());
			//Print($"Raycast {cam.EnableRaycast} {cam.RaycastDistanceLimit}");
			Print($"Scan range {cam.AvailableScanRange / 1000.0:0.}km");

			if (!checkBlocks()) { return; }
			if (arg == "target")
			{
				//var detected = cam.Raycast(cam.RaycastDistanceLimit - 1);
				var detected = cam.Raycast(cam.AvailableScanRange - 1);
				if (detected.IsEmpty())
				{
					msg($"No entity within {cam.RaycastDistanceLimit}m");
				}
				else
				{
					var co = detected.HitPosition.Value;
					//GPS:target:-803.98:61786.97:1009.19:#FF75C9F1:
					// color is ARGB
					string gps = $"GPS:{detected.Name}:{co.X}:{co.Y}:{co.Z}:##FFFF0000:";
					IGC.SendBroadcastMessage(RadioChannel, gps);
				}
			}
			else if (updateSource == UpdateType.Update1 ||
				updateSource == UpdateType.Update10 ||
				updateSource == UpdateType.Update100)
			{
				while (IGC.UnicastListener.HasPendingMessage)
				{
					var rmsg = IGC.UnicastListener.AcceptMessage();
					var s = rmsg.As<string>();
					//rmsg.Source
					//msg($"{rmsg.Source % 1000}: {s}");
					//Message msg = new Message();
					//msg.when = DateTime.Now;
					//msg.text = rmsg.As<string>();
					//messages.Enqueue(msg);
					GunStatus st;
					if (s.StartsWith("linked"))
					{
						st = new GunStatus
						{
							id = rmsg.Source,
							lastMessageTime = DateTime.Now,
							state = SmartyProgramState.Idle
						};
						//linkCount++;
						status[rmsg.Source] = st;
					}
					else if (s.StartsWith("unlinked"))
					{
						//linkCount--;
						status.Remove(rmsg.Source);
					}
					else if (s.StartsWith("state:"))
					{
						var stateSplit = s.Split(':');
						if (stateSplit.Length == 3)
						{
							if (!status.ContainsKey(rmsg.Source))
							{
								st = new GunStatus
								{
									id = rmsg.Source,
									lastMessageTime = DateTime.Now,
									state = v2e<SmartyProgramState>(stateSplit[1]),
									steady = bool.Parse(stateSplit[2])
								};
								status[rmsg.Source] = st;
							}
							else
							{
								st = status[rmsg.Source];
								st.state = v2e<SmartyProgramState>(stateSplit[1]);
								st.lastMessageTime = DateTime.Now;
								st.steady = bool.Parse(stateSplit[2]);
							}
							st = status[rmsg.Source];
						}
					}
					//if (linkCount < 0) { linkCount = 0; }
				} // while has pending msg
			} // if updatesource == Update1/10/100
			else
			{
				IGC.SendBroadcastMessage(RadioChannel, arg);
			}


			// Display (semi)persistent messages
			//for (int imsg = 0; imsg < messages.Count; imsg++)
			for (int imsg = messages.Count - 1; imsg >= 0; imsg--)
			{
				Print(messages.ElementAt(imsg).text);
			}
			while (messages.Count > 0 && messages.Peek().when + TimeSpan.FromSeconds(messageTimeout) < DateTime.Now)
			{
				//Print("removed msg");
				messages.Dequeue();
			}

			RunLCD();
		}



		E v2e<E>(string s) // Var (string) to enum
		{
			//MyAssemblerMode m = MyAssemblerMode.Assembly;
			//foreach (var x in Enum.GetValues(typeof(MyAssemblerMode)))
			foreach (var x in Enum.GetValues(typeof(E)))
			{
				if (s == x.ToString()) { return (E)x; }
			}
			throw new InvalidCastException();
		}












		bool checkBlocks()
		{
			bool ret = true;
			if (cam == null)
			{
				Print("Camera block not found");
				ret = false;
			}
			if (ant == null)
			{
				Print("Antenna not found");
				ret = false;
			}
			return ret;
		}

		struct Message
		{
			public DateTime when;
			public string text;
		}
		Queue<Message> messages = new Queue<Message>();
		void msg(string s)
		{
			messages.Enqueue(new Message { when = DateTime.Now, text = s });
			Print(s);
			//msg(messages.ElementAt(0).text);
		}
		void Print(string s)
		{
			Echo(s);
			printBuf.Append(s);
			printBuf.Append("\r\n");
			// TODO: LCD
		}

		const int SPACING = 5;
		float loadingSpriteAngle = 0;
		void RunLCD()
		{
			if (disp != null && status.Count > 0)
			{
				//disp.WriteText(printBuf, false);
				//disp.ContentType = ContentType.TEXT_AND_IMAGE;
				//IMyTextPanel p; p.DrawFrame // testing, ignore
				int gunCount = status.Count;
				float aspect = disp.SurfaceSize.X / disp.SurfaceSize.Y;
				int rows = (int)Math.Ceiling(Math.Sqrt(gunCount / aspect));
				var frame = disp.DrawFrame();
				Vector2 size;
				//size.X = disp.SurfaceSize.X / columnCount - 5;
				//size.X = (float)Math.Max(5, (float)Math.Sqrt(surfacePixels / gunCount) - 5);
				size.X = size.Y = disp.SurfaceSize.Y / rows - SPACING; // size.X;

				Vector2 pos = (disp.TextureSize - disp.SurfaceSize) / 2;
				pos.X += SPACING + size.X / 2;
				pos.Y += SPACING + size.Y / 2;
				//pos = disp.TextureSize / 2;
				//Echo($"tex {disp.TextureSize}");
				//Echo($"surf {disp.SurfaceSize}");
				//Echo($"pos = {pos.X}, {pos.Y}");
				MySprite sprite;

				//sprite = new MySprite()
				//{
				//	Type = SpriteType.TEXT,
				//	Data = $"{status.Count} guns online",
				//	Position = pos,
				//	Size = size,
				//	RotationOrScale = 2.8f,
				//	Color = Color.Red,
				//	Alignment = TextAlignment.LEFT, /* Center the text on the position */
				//	//FontId = "White"
				//};
				//frame.Add(sprite);
				//pos.Y += disp.Get

				loadingSpriteAngle += 0.4f;

				float angle = 0;
				Vector2 resetPos = pos;
				for (int i = 0; i < gunCount; i++)
				{
					sprite = new MySprite
					{
						Type = SpriteType.TEXTURE,
						Data = "SquareHollow",
						Position = pos,
						Size = size,
						Color = Color.Gray.Alpha(0.5f),
						Alignment = TextAlignment.CENTER,
						//FontId = "",
						//RotationOrScale = 0
					};
					frame.Add(sprite);
					pos.X += size.X + SPACING;
					if ((pos.X + size.X) > (disp.SurfaceSize.X - size.X / 2))
					{
						pos.X = (disp.TextureSize.X - disp.SurfaceSize.X) / 2 + SPACING + size.X / 2;
						pos.Y += size.Y + SPACING;
					}
				}
				pos = resetPos;


				// fontPxHeight * F = size.Y * HeightFactor
				//
				const float TEXT_HEIGHT_FACTOR = .4f;
				float measuredHeight = disp.MeasureStringInPixels(new StringBuilder("Mq"), "White", 1.0f).Y;
				//float fontSize = (meas / disp.SurfaceSize.Y) * size.Y * TEXT_HEIGHT_FACTOR;
				//Echo($"{fontSize} = ({meas} / {disp.SurfaceSize.Y}) * {size.Y} * {TEXT_HEIGHT_FACTOR};");
				float fontSize = size.Y * TEXT_HEIGHT_FACTOR / measuredHeight;
				Echo($"{fontSize} = {size.Y} * {TEXT_HEIGHT_FACTOR} / {measuredHeight};");

				foreach (var st in status)
				{
					Color col = Color.Gray.Alpha(0.5f);
					string spriteName = "SquareHollow";
					switch (st.Value.state)
					{
						case SmartyProgramState.Aiming:
							if (st.Value.steady)
							{
								col = Color.Green;
								spriteName = "Arrow";
							}
							else
							{
								col = Color.Yellow;
								spriteName = "Screen_LoadingBar";
								angle = loadingSpriteAngle;
							}
							col = (st.Value.steady ? Color.Green : Color.Yellow);
							break;
						case SmartyProgramState.Firing:
							//col = Color.Magenta;
							//spriteName = "MyObjectBuilder_AmmoMagazine/LargeCalibreAmmo";
							col = Color.White; // sprite is colored
							spriteName = "Danger";
							break;
						case SmartyProgramState.Offline:
							//spriteName = "No Entry";
							spriteName = "Cross";
							col = Color.DarkRed;
							break;
						case SmartyProgramState.Stowing:
							spriteName = "Screen_LoadingBar";
							angle = loadingSpriteAngle;
							col = Color.Yellow;
							break;
						case SmartyProgramState.Idle:
							col = Color.Lerp(Color.Red, Color.Orange, 0.3f);
							spriteName = "Circle";
							break;
						case SmartyProgramState.OutOfRange:
							col = Color.White; // sprite is colored
							spriteName = "No Entry";
							break;
						default:
							col = Color.Gray;
							spriteName = "SquareHollow";
							break;
					}
					sprite = new MySprite
					{
						Type = SpriteType.TEXTURE,
						Data = spriteName,
						Position = pos,
						Size = size,
						Color = col, //Color.Purple.Alpha(0.66f),
						Alignment = TextAlignment.CENTER,
						RotationOrScale = angle,
						//FontId = "White",
					};
					frame.Add(sprite);

					sprite = new MySprite
					{
						Type = SpriteType.TEXT,
						Data = (st.Value.id % 1000).ToString(),
						Position = pos,
						Size = size,
						Color = Color.White,
						Alignment = TextAlignment.CENTER,
						//RotationOrScale = size.Y / 10.0f,
						RotationOrScale = fontSize,
						FontId = "White"
					};
					frame.Add(sprite);

					pos.X += size.X + SPACING;
					if ((pos.X + size.X) > (disp.SurfaceSize.X - size.X / 2))
					{
						pos.X = (disp.TextureSize.X - disp.SurfaceSize.X) / 2 + SPACING + size.X / 2;
						pos.Y += size.Y + SPACING;
					}
				}


				frame.Dispose();
			} // if disp
			printBuf.Clear();

			// Purge offline guns
			bool found = true;
			while (found)
			{
				found = false;
				foreach (var kv in status)
				{
					if (kv.Value.lastMessageTime + GunDisplayTimeout < DateTime.Now)
					{
						found = true;
						status.Remove(kv.Key);
						break;
					}
				}
			}
		}


		char[] SpinnerChars = { '/', '-', '\\', '|' };
		int spinnerIndex = 0;
		char Spinner()
		{
			spinnerIndex = (spinnerIndex + 1) % 4;
			return SpinnerChars[spinnerIndex];
		}
	}
}
