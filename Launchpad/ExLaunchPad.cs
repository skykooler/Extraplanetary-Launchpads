using System;
using System.Collections.Generic;
using System.Linq;
//using System.IO;	  // needed for Path manipulation
//using Uri;
using UnityEngine;

using KSP.IO;

using ExLP;		// until everything is properly namespaced?

/// <summary>
/// TODO
/// </summary>
public class ExLaunchPad : PartModule
{

	[KSPField]
	public bool debug = false;


	public enum crafttype { SPH, VAB };

	public class UIStatus
	{
		public Rect windowpos;
		public bool builduiactive = false;	// Whether the build menu is open or closed
		public bool builduivisible = true;	// Whether the build menu is allowed to be shown
		public bool showbuilduionload = false;
		public bool init = true;
		public bool linklfosliders = true;
		public bool showvab = true;
		public bool showsph = false;
		public bool canbuildcraft = false;
		public crafttype ct = crafttype.VAB;
		public string craftfile = null;
		public string flagname = null;
		public CraftBrowser craftlist = null;
		public bool showcraftbrowser = false;
		public ConfigNode craftnode = null;
		public bool craftselected = false;
		public Vector2 resscroll;
		public Dictionary<string, double> requiredresources = null;
		public double minRocketParts = 0.0;
		public Dictionary<string, float> resourcesliders = new Dictionary<string, float>();

		public float timer;
		public Vessel vessel;
	}

	int padPartsCount;					// the number of parts in the pad vessel (for docking detection)
	VesselResources padResources;		// resources available to the pad

	[KSPField(isPersistant = false)]
	public float SpawnHeightOffset = 1.0f;	// amount of pad between origin and open space

	private UIStatus uis = new UIStatus();

	//private List<Vessel> bases;

	// =====================================================================================================================================================
	// UI Functions

	private void UseResources(Vessel craft)
	{
		VesselResources craftResources = new VesselResources(craft);
		craftResources.RemoveAllResources();

		// Solid Fuel is always full capacity, so put it all back
		craftResources.TransferResource("SolidFuel", craftResources.ResourceCapacity("SolidFuel"));

		// remove rocket parts required for the hull and solid fuel
		padResources.TransferResource("RocketParts", -uis.minRocketParts);

		// use resources
		foreach (KeyValuePair<string, double> pair in uis.requiredresources) {
			// If resource is "JetFuel", rename to "LiquidFuel"
			string res = pair.Key;
			if (pair.Key == "JetFuel") {
				res = "LiquidFuel";
				if (pair.Value == 0)
					continue;
			}
			if (!uis.resourcesliders.ContainsKey(pair.Key)) {
				Debug.Log(String.Format("[EL] missing slider {0}", pair.Key));
				continue;
			}
			// Calculate resource cost based on slider position - note use pair.Key NOT res! we need to use the position of the dedicated LF slider not the LF component of LFO slider
			double tot = pair.Value * uis.resourcesliders[pair.Key];
			if (pair.Key == "RocketParts") {
				// don't transfer rocket parts required for hull and solid fuel.
				tot -= uis.minRocketParts;
				if (tot < 0.0)
					tot = 0.0;
			}
			// Transfer the resource from the vessel doing the building to the vessel being built
			padResources.TransferResource(res, -tot);
			craftResources.TransferResource(res, tot);
		}
	}

	private void FixCraftLock()
	{
		// Many thanks to Snjo (firespitter)
		uis.vessel.situation = Vessel.Situations.LANDED;
		uis.vessel.state = Vessel.State.ACTIVE;
		uis.vessel.Landed = true;
		uis.vessel.Splashed = false;
		uis.vessel.GoOnRails();
		uis.vessel.rigidbody.WakeUp();
		uis.vessel.ResumeStaging();
		uis.vessel.landedAt = "External Launchpad";
		InputLockManager.ClearControlLocks();
	}

	private void BuildAndLaunchCraft()
	{
		// build craft
		ShipConstruct nship = ShipConstruction.LoadShip(uis.craftfile);

		Vector3 offset = Vector3.up * SpawnHeightOffset;
		Transform t = this.part.transform;

		string landedAt = "External Launchpad";
		string flag = uis.flagname;
		Game state = FlightDriver.FlightStateCache;
		VesselCrewManifest crew = new VesselCrewManifest ();

		GameObject launchPos = new GameObject ();
		launchPos.transform.position = t.position;
		launchPos.transform.position += t.TransformDirection(offset);
		launchPos.transform.rotation = t.rotation;
		ShipConstruction.CreateBackup(nship);
		ShipConstruction.PutShipToGround(nship, launchPos.transform);
		Destroy(launchPos);

		ShipConstruction.AssembleForLaunch(nship, landedAt, flag, state, crew);

		Vessel vessel = FlightGlobals.ActiveVessel;
		vessel.Landed = false;

		if (!debug)
			UseResources(vessel);

		Staging.beginFlight();

		uis.timer = 3.0f;
		uis.vessel = vessel;
	}

	private void WindowGUI(int windowID)
	{
		/*
		 * ToDo:
		 * can extend FileBrowser class to see currently highlighted file?
		 * rslashphish says: public myclass(arg1, arg2) : base(arg1, arg2);
		 * KSPUtil.ApplicationRootPath - gets KSPO root
		 * expose m_files and m_selectedFile?
		 * fileBrowser = new FileBrowser(new Rect(Screen.width / 2, 100, 350, 500), title, callback, true);
		 *
		 * Style declarations messy - how do I dupe them easily?
		 */
		if (uis.init)
		{
			uis.init = false;
		}

		EditorLogic editor = EditorLogic.fetch;
		if (editor) return;

		if (!uis.builduiactive) return;

		if (padResources != null && padPartsCount != vessel.Parts.Count) {
			// something docked or undocked, so rebuild the pad's resouces info
			padResources = null;
		}
		if (padResources == null) {
			padPartsCount = vessel.Parts.Count;
			padResources = new VesselResources(vessel);
		}

		GUIStyle mySty = new GUIStyle(GUI.skin.button);
		mySty.normal.textColor = mySty.focused.textColor = Color.white;
		mySty.hover.textColor = mySty.active.textColor = Color.yellow;
		mySty.onNormal.textColor = mySty.onFocused.textColor = mySty.onHover.textColor = mySty.onActive.textColor = Color.green;
		mySty.padding = new RectOffset(8, 8, 8, 8);

		GUIStyle redSty = new GUIStyle(GUI.skin.box);
		redSty.padding = new RectOffset(8, 8, 8, 8);
		redSty.normal.textColor = redSty.focused.textColor = Color.red;

		GUIStyle yelSty = new GUIStyle(GUI.skin.box);
		yelSty.padding = new RectOffset(8, 8, 8, 8);
		yelSty.normal.textColor = yelSty.focused.textColor = Color.yellow;

		GUIStyle grnSty = new GUIStyle(GUI.skin.box);
		grnSty.padding = new RectOffset(8, 8, 8, 8);
		grnSty.normal.textColor = grnSty.focused.textColor = Color.green;

		GUIStyle whiSty = new GUIStyle(GUI.skin.box);
		whiSty.padding = new RectOffset(8, 8, 8, 8);
		whiSty.normal.textColor = whiSty.focused.textColor = Color.white;

		GUIStyle labSty = new GUIStyle(GUI.skin.label);
		labSty.normal.textColor = labSty.focused.textColor = Color.white;
		labSty.alignment = TextAnchor.MiddleCenter;

		GUILayout.BeginVertical();

		GUILayout.BeginHorizontal("box");
		GUILayout.FlexibleSpace();
		// VAB / SPH selection
		if (GUILayout.Toggle(uis.showvab, "VAB", GUILayout.Width(80))) {
			uis.showvab = true;
			uis.showsph = false;
			uis.ct = crafttype.VAB;
		}
		if (GUILayout.Toggle(uis.showsph, "SPH")) {
			uis.showvab = false;
			uis.showsph = true;
			uis.ct = crafttype.SPH;
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();

		string strpath = HighLogic.CurrentGame.Title.Split(new string[] { " (Sandbox)" }, StringSplitOptions.None).First();

		if (GUILayout.Button("Select Craft", mySty, GUILayout.ExpandWidth(true))) {
			//GUILayout.Button is "true" when clicked
			uis.craftlist = new CraftBrowser(new Rect(Screen.width / 2, 100, 350, 500), uis.ct.ToString(), strpath, "Select a ship to load", craftSelectComplete, craftSelectCancel, HighLogic.Skin, EditorLogic.ShipFileImage, true);
			uis.showcraftbrowser = true;
		}

		if (uis.craftselected) {
			GUILayout.Box("Selected Craft:	" + uis.craftnode.GetValue("ship"), whiSty);

			// Resource requirements
			GUILayout.Label("Resources required to build:", labSty, GUILayout.Width(600));

			// Link LFO toggle

			uis.linklfosliders = GUILayout.Toggle(uis.linklfosliders, "Link RocketFuel sliders for LiquidFuel and Oxidizer");

			uis.resscroll = GUILayout.BeginScrollView(uis.resscroll, GUILayout.Width(600), GUILayout.Height(300));

			GUILayout.BeginHorizontal();

			// Headings
			GUILayout.Label("Resource", labSty, GUILayout.Width(120));
			GUILayout.Label("Fill Percentage", labSty, GUILayout.Width(300));
			GUILayout.Label("Required", labSty, GUILayout.Width(75));
			GUILayout.Label("Available", labSty, GUILayout.Width(75));
			GUILayout.EndHorizontal();

			uis.canbuildcraft = true;	   // default to can build - if something is stopping us from building, we will set to false later

			// LFO = 55% oxidizer

			// Cycle through required resources
			foreach (KeyValuePair<string, double> pair in uis.requiredresources) {
				string resname = pair.Key;	// Holds REAL resource name. May need to translate from "JetFuel" back to "LiquidFuel"
				string reslabel = resname;	 // Resource name for DISPLAY purposes only. Internally the app uses pair.Key
				if (reslabel == "JetFuel") {
					if (pair.Value == 0f) {
						// Do not show JetFuel line if not being used
						continue;
					}
					//resname = "JetFuel";
					resname = "LiquidFuel";
				}

				// If in link LFO sliders mode, rename Oxidizer to LFO (Oxidizer) and LiquidFuel to LFO (LiquidFuel)
				if (reslabel == "Oxidizer") {
					reslabel = "RocketFuel (Ox)";
				}
				if (reslabel == "LiquidFuel") {
					reslabel = "RocketFuel (LF)";
				}

				GUILayout.BeginHorizontal();

				// Resource name
				GUILayout.Box(reslabel, whiSty, GUILayout.Width(120), GUILayout.Height(40));
				if (!uis.resourcesliders.ContainsKey(pair.Key)) {
					uis.resourcesliders.Add(pair.Key, 1);
				}

				GUIStyle tmpSty = new GUIStyle(GUI.skin.label);
				tmpSty.alignment = TextAnchor.MiddleCenter;
				tmpSty.margin = new RectOffset(0, 0, 0, 0);
				GUIStyle sliSty = new GUIStyle(GUI.skin.horizontalSlider);
				sliSty.margin = new RectOffset(0, 0, 0, 0);
				// Fill amount
				GUILayout.BeginVertical();
				do {
					float minFrac = 0.0f;

					if (pair.Key == "RocketParts") {
						if (uis.requiredresources["RocketParts"] <= uis.minRocketParts) {
							// Partial Fill for RocketParts not allowed - Instead of creating a slider, hard-wire slider position to 100%
							uis.resourcesliders[pair.Key] = 1;
							GUILayout.FlexibleSpace();
							GUILayout.Box("Must be 100%", GUILayout.Width(300), GUILayout.Height(20));
							GUILayout.FlexibleSpace();
							break;
						}
						// However, the craft might have RocketParts storage, which may be partially filled. 
						minFrac = (float) (uis.minRocketParts / uis.requiredresources["RocketParts"]);
					}
					GUILayout.FlexibleSpace();
					// limit slider to 0.5% increments
					float tmp = (float)Math.Round(GUILayout.HorizontalSlider(uis.resourcesliders[pair.Key], minFrac, 1.0F, sliSty, new GUIStyle(GUI.skin.horizontalSliderThumb), GUILayout.Width(300), GUILayout.Height(20)), 3);
					tmp = (Mathf.Floor(tmp * 200)) / 200;

					// Are we in link LFO mode?
					if (uis.linklfosliders) {
						if (pair.Key == "Oxidizer") {
							uis.resourcesliders["LiquidFuel"] = tmp;
						} else if (pair.Key == "LiquidFuel") {
							uis.resourcesliders["Oxidizer"] = tmp;
						}
					}
					// Assign slider value to variable
					uis.resourcesliders[pair.Key] = tmp;
					GUILayout.Box((tmp * 100).ToString() + "%", tmpSty, GUILayout.Width(300), GUILayout.Height(20));
					GUILayout.FlexibleSpace();
				} while (false);
				GUILayout.EndVertical();


				// Calculate if we have enough resources to build
				double tot = padResources.ResourceAmount(resname);

				// If LFO LiquidFuel exists and we are on LiquidFuel (Non-LFO), then subtract the amount used by LFO(LiquidFuel) from the available amount

				if (pair.Key == "JetFuel") {
					tot -= uis.requiredresources["LiquidFuel"] * uis.resourcesliders["LiquidFuel"];
					if (tot < 0.0)
						tot = 0.0;
				}
				GUIStyle avail = new GUIStyle();
				if (tot < pair.Value * uis.resourcesliders[pair.Key]) {
					avail = redSty;
					uis.canbuildcraft = (false || debug); // prevent building unless debug mode is on
				} else {
					avail = grnSty;
				}

				// Required
				GUILayout.Box((Math.Round(pair.Value * uis.resourcesliders[pair.Key], 2)).ToString(), avail, GUILayout.Width(75), GUILayout.Height(40));
				// Available
				GUILayout.Box(((int)tot).ToString(), whiSty, GUILayout.Width(75), GUILayout.Height(40));

				// Flexi space to make sure any unused space is at the right-hand edge
				GUILayout.FlexibleSpace();

				GUILayout.EndHorizontal();
			}

			GUILayout.EndScrollView();

			// Build button
			if (uis.canbuildcraft) {
				if (GUILayout.Button("Build", mySty, GUILayout.ExpandWidth(true))) {
					BuildAndLaunchCraft();
					// Reset the UI
					uis.craftselected = false;
					uis.requiredresources = null;
					uis.resourcesliders = new Dictionary<string, float>();;

					// Close the UI
					HideBuildMenu();
				}
			} else {
				GUILayout.Box("You do not have the resources to build this craft", redSty);
			}
		} else {
			GUILayout.Box("You must select a craft before you can build", redSty);
		}
		GUILayout.EndVertical();

		GUILayout.BeginHorizontal();
		GUILayout.FlexibleSpace();
		if (GUILayout.Button("Close")) {
			HideBuildMenu();
		}

		uis.showbuilduionload = GUILayout.Toggle(uis.showbuilduionload, "Show on StartUp");

		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		//DragWindow makes the window draggable. The Rect specifies which part of the window it can by dragged by, and is
		//clipped to the actual boundary of the window. You can also pass no argument at all and then the window can by
		//dragged by any part of it. Make sure the DragWindow command is AFTER all your other GUI input stuff, or else
		//it may "cover up" your controls and make them stop responding to the mouse.
		GUI.DragWindow(new Rect(0, 0, 10000, 20));

	}

	// called when the user selects a craft the craft browser
	private void craftSelectComplete(string filename, string flagname)
	{
		uis.showcraftbrowser = false;
		uis.craftfile = filename;
		uis.flagname = flagname;
		uis.craftnode = ConfigNode.Load(filename);
		ConfigNode[] nodes = uis.craftnode.GetNodes("PART");

		// Get list of resources required to build vessel
		if ((uis.requiredresources = getBuildCost(nodes)) != null)
			uis.craftselected = true;
	}

	// called when the user clicks cancel in the craft browser
	private void craftSelectCancel()
	{
		uis.showcraftbrowser = false;

		uis.requiredresources = null;
		uis.craftselected = false;
	}

	// =====================================================================================================================================================
	// Event Hooks
	// See http://docs.unity3d.com/Documentation/Manual/ExecutionOrder.html for some help on what fires when

	// Called each time the GUI is painted
	private void drawGUI()
	{
		GUI.skin = HighLogic.Skin;
		uis.windowpos = GUILayout.Window(1, uis.windowpos, WindowGUI, "Extraplanetary Launchpads", GUILayout.Width(600));
	}

	// Called ONCE at start
	private void Start()
	{
		// If "Show GUI on StartUp" ticked, show the GUI
		if (uis.showbuilduionload) {
			ShowBuildMenu();
		}
	}


	// Fired maybe multiple times a frame, maybe once every n frames
	public override void OnFixedUpdate()
	{
		// ToDo: Should not be checking this every frame - once per craft switch
		// OnVesselChange may be ideal but I cannot seem to get it to fire
		// Landed / Flying check should probably be with this code, but moved it elsewhere while this is firing so often

		// Does the UI want to be visible?
		if (uis.builduiactive) {
			// Decide if the build menu is allowed to be visible
			if (this.vessel == FlightGlobals.ActiveVessel) {
				// Yes - check if it is currently not visible
				if (!uis.builduivisible) {
					// Going from invisible to visible
					uis.builduivisible = true;
					RenderingManager.AddToPostDrawQueue(3, new Callback(drawGUI)); //start the GUI
				}
			} else {
				// No - check if it is currently visible
				if (uis.builduivisible) {
					// Going from visible to invisible
					uis.builduivisible = false;
					RenderingManager.RemoveFromPostDrawQueue(3, new Callback(drawGUI)); //stop the GUI
				}
			}
		}
	}

	/*
	// Called when you change vessel
	// ToDo: Cannot seem to get this code to fire...
	private void OnVesselChange()
	{
		if (this.vessel == FlightGlobals.ActiveVessel) {
			ShowBuildMenu();
		} else {
			HideBuildMenu();
		}
	}
	*/

	public void Update()
	{
		if (uis.vessel && uis.timer >= 0) {
			uis.timer -= Time.deltaTime;
			if (uis.timer <= 0) {
				FixCraftLock();
				uis.vessel = null;
			}
		}
	}

	// Fired ONCE per frame
	public override void OnUpdate()
	{
		// Update state of context buttons depending on state of UI
		// ToDo: Move to something fired when the GUI is updated?
		Events["ShowBuildMenu"].active = !uis.builduiactive;
		Events["HideBuildMenu"].active = uis.builduiactive;
	}

	// Fired multiple times per frame in response to GUI events
	private void OnGUI()
	{
		if (uis.showcraftbrowser) {
			uis.craftlist.OnGUI();
		}
	}

	/*
	// ToDo: What Does this Do?
	private void OnLoad()
	{
		bases = FlightGlobals.fetch.vessels;
		foreach (Vessel v in bases) {
			print(v.name);
		}
	}
	*/

	// Fired when KSP saves
	public override void OnSave(ConfigNode node)
	{
		PluginConfiguration config = PluginConfiguration.CreateForType<ExLaunchPad>();
		config.SetValue("Window Position", uis.windowpos);
		config.SetValue("Show Build Menu on StartUp", uis.showbuilduionload);
		config.save();
	}


	// Fired when KSP loads
	public override void OnLoad(ConfigNode node)
	{
		LoadConfigFile();
	}

	private void LoadConfigFile()
	{
		PluginConfiguration config = PluginConfiguration.CreateForType<ExLaunchPad>();
		config.load();
		uis.windowpos = config.GetValue<Rect>("Window Position");
		uis.showbuilduionload = config.GetValue<bool>("Show Build Menu on StartUp");
	}

	// =====================================================================================================================================================
	// Flight UI and Action Group Hooks

	[KSPEvent(guiActive = true, guiName = "Show Build Menu", active = true)]
	public void ShowBuildMenu()
	{
		// Only allow enabling the menu if we are in a suitable place
		if (((this.vessel.situation == Vessel.Situations.LANDED) ||
				(this.vessel.situation == Vessel.Situations.PRELAUNCH) ||
				(this.vessel.situation == Vessel.Situations.SPLASHED))) {
			RenderingManager.AddToPostDrawQueue(3, new Callback(drawGUI)); //start the GUI
			uis.builduiactive = true;
		}
	}

	[KSPEvent(guiActive = true, guiName = "Hide Build Menu", active = false)]
	public void HideBuildMenu()
	{
		RenderingManager.RemoveFromPostDrawQueue(3, new Callback(drawGUI)); //stop the GUI
		uis.builduiactive = false;
	}

	[KSPAction("Show Build Menu")]
	public void EnableBuildMenuAction(KSPActionParam param)
	{
		ShowBuildMenu();
	}

	[KSPAction("Hide Build Menu")]
	public void DisableBuildMenuAction(KSPActionParam param)
	{
		HideBuildMenu();
	}

	[KSPAction("Toggle Build Menu")]
	public void ToggleBuildMenuAction(KSPActionParam param)
	{
		if (uis.builduiactive) {
			HideBuildMenu();
		} else {
			ShowBuildMenu();
		}
	}

	// =====================================================================================================================================================
	// Build Helper Functions

	private void MissingPopup(Dictionary<string, bool> missing_parts)
	{
		string text = "";
		foreach (string mp in missing_parts.Keys)
			text += mp + "\n";
		int ind = uis.craftfile.LastIndexOf("/") + 1;
		string craft = uis.craftfile.Substring (ind);
		craft = craft.Remove (craft.LastIndexOf("."));
		PopupDialog.SpawnPopupDialog("Sorry", "Can't build " + craft + " due to the following missing parts\n\n" + text, "OK", false, HighLogic.Skin);
	}

	public Dictionary<string, double> getBuildCost(ConfigNode[] nodes)
	{
		float mass = 0.0f;
		Dictionary<string, double> resources = new Dictionary<string, double>();
		Dictionary<string, bool> missing_parts = new Dictionary<string, bool>();

		resources["RocketParts"] = 0.0;	// Ensure there is a RocketParts entry.

		foreach (ConfigNode node in nodes) {
			string part_name = node.GetValue("part");
			part_name = part_name.Remove(part_name.LastIndexOf("_"));
			AvailablePart ap = PartLoader.getPartInfoByName(part_name);
			if (ap == null) {
				missing_parts[part_name] = true;
				continue;
			}
			Part p = ap.partPrefab;
			mass += p.mass;
			foreach (PartResource r in p.Resources) {
				if (r.resourceName == "IntakeAir") {
					// Ignore intake Air
					continue;
				}

				if (!resources.ContainsKey(r.resourceName)) {
					resources[r.resourceName] = 0.0;
				}
				resources[r.resourceName] += r.maxAmount;
			}
		}
		if (missing_parts.Count > 0) {
			MissingPopup(missing_parts);
			return null;
		}
		PartResourceDefinition rpdef;
		rpdef = PartResourceLibrary.Instance.GetDefinition("RocketParts");
		uis.minRocketParts = mass / rpdef.density;
		resources["RocketParts"] += uis.minRocketParts;


		// If Solid Fuel is used, convert to RocketParts
		if (resources.ContainsKey("SolidFuel")) {
			PartResourceDefinition sfdef;
			sfdef = PartResourceLibrary.Instance.GetDefinition("SolidFuel");
			double sfmass = resources["SolidFuel"] * sfdef.density;
			double sfparts = sfmass / rpdef.density;
			resources["RocketParts"] += sfparts;
			uis.minRocketParts += sfparts;
			resources.Remove("SolidFuel");
		}

		// If there is JetFuel (ie LF only tanks as well as LFO tanks - eg a SpacePlane) then split the Surplus LF off as "JetFuel"
		if (resources.ContainsKey("Oxidizer") && resources.ContainsKey("LiquidFuel")) {
			double jetFuel = 0.0;
			// The LiquidFuel:Oxidizer ratio is 9:11. Try to minimize rounding effects.
			jetFuel = (11 * resources["LiquidFuel"] - 9 * resources["Oxidizer"]) / 11;
			if (jetFuel < 0.01)	{
				// Forget it. not getting far on that. Any discrepency this
				// small will be due to precision losses.
				jetFuel = 0.0;
			}
			resources["LiquidFuel"] -= jetFuel;
			resources["JetFuel"] = jetFuel;
		}

		return resources;
	}

	// =====================================================================================================================================================
	// Unused

	/*
	 * A simple test to see if other DLLs can call funcs
	 * to use - add reference to this dll in other project and then use this code:
	 *
	 ExLaunchPad exl = new ExLaunchPad();
			string tmp = exl.evilCTest();
	*/
	public string evilCTest()
	{
		return "Hello!";
	}

	private void destroyShip(ShipConstruct nship, float availableRocketParts, float availableLiquidFuel, float availableOxidizer, float availableMonoPropellant)
	{
		this.part.RequestResource("RocketParts", -availableRocketParts);
		this.part.RequestResource("LiquidFuel", -availableLiquidFuel);
		this.part.RequestResource("Oxidizer", -availableOxidizer);
		this.part.RequestResource("MonoPropellant", -availableMonoPropellant);
		nship.parts[0].localRoot.explode();
	}
}

public class Recycler : PartModule
{
	[KSPEvent(guiActive = true, guiName = "Recycle Debris", active = true)]
	public void RemoveDebris()
	{
		float conversionEfficiency = 0.8f;
		List<Vessel> tempList = new List<Vessel>(); //temp list to hold debris vessels
		VesselResources recycler = new VesselResources(vessel);
		PartResourceDefinition rpdef;
		rpdef = PartResourceLibrary.Instance.GetDefinition("RocketParts");
		double amount, remain;

		foreach (Vessel v in FlightGlobals.Vessels) {
			if (v.vesselType == VesselType.Debris) tempList.Add(v);
		}
		foreach (Vessel v in tempList) {
			// If vessel is less than 50m away, delete and convert it to rocketparts at conversionEfficiency% efficiency
			if (Vector3d.Distance(v.GetWorldPos3D(), this.vessel.GetWorldPos3D())<50) {
				VesselResources scrap = new VesselResources(v);
				foreach (string resource in scrap.resources.Keys) {
					remain = amount = scrap.ResourceAmount (resource);
					// Pul out solid fuel, but lose it.
					scrap.TransferResource(resource, -amount);
					if (resource != "SolidFuel") {
						// anything left over just evaporates
						remain = recycler.TransferResource(resource, amount);
					}
					Debug.Log(String.Format("[EL] {0}-{1}: {2} taken {3} reclaimed, {4} lost", v.name, resource, amount, amount - remain, remain));
				}
				float mass = v.GetTotalMass();
				amount = mass * conversionEfficiency / rpdef.density;
				remain = recycler.TransferResource("RocketParts", amount);
				Debug.Log(String.Format("[EL] {0}: hull rocket parts {1} taken {2} reclaimed {3} lost", v.name, amount, amount - remain, remain));
				v.Die();
			}
		}
	}
}
/*
 * //TODO : make this work
[KSPAddon(KSPAddon.Startup.EditorAny, false)]
public class LaunchSiteSelector : MonoBehaviour
{
	void Start()
	{
		// Generate a list of your currently deployed vessels with the launchpad and name them for a dictionary. E.g. "LaunchPad_LpID", "LaunchPad 01 : Mun [Lat/Long]"
	}

	void OnGUI()
	{
		// Make pretty GUI with a button that does the following:
		//EditorLogic.fetch.launchSiteName = SelectedLaunchSiteGameObjectName
		EditorLogic.fetch.launchSiteName = "Dubig";
	}
}

[KSPAddon(KSPAddon.Startup.Flight, false)]
public class LaunchSite : MonoBehaviour
{
	void Awake()
	{
		//GameObject gm = new GameObject(LaunchSiteID + "_spawn");
		GameObject gm = new GameObject("Dubig" + "_spawn");
		//gm.transform.position = vesselWorldTransformSpawningPoint.position;
		//gm.transform.rotation= vesselWorldTransformSpawningPoint.rotation;

		foreach (Vessel v in FlightGlobals.Vessels) {
			if (v.name == "Dubig") {
				gm.transform.position = v.GetTransform().position;
				gm.transform.rotation = v.GetTransform().rotation;
			}
		}
		gm.SetActive(true);
	}
}*/
