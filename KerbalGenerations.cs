using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;

namespace KerbalGenerations
{
    // ----------------------------------------------------------------------
    // 1. SCENARIO MODULE: Handles Saving and Loading Data (Family Tree + Settings)
    // ----------------------------------------------------------------------
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT, GameScenes.SPACECENTER)]
    public class GenerationsData : ScenarioModule
    {
        public static GenerationsData Instance;
        
        // --- Persistent Settings ---
        public bool BreedingEnabled = true;
        public int GenderMode = 0; // 0=Random, 1=Male, 2=Female
        public float BreedingSpeed = 10.0f; 
        public int MaxConcurrentPregnancies = 1;
        public double ChildhoodDuration = 9201600; // Default 1 Year (seconds)

        // Stores family ties: Child Name -> Parent Names
        public Dictionary<string, FamilyLink> FamilyTree = new Dictionary<string, FamilyLink>();
        
        // Stores babies currently growing up: Kerbal Name -> Birth Time
        public Dictionary<string, double> GrowingBabies = new Dictionary<string, double>();

        // Stores active pregnancies: Mother Name -> Seconds Progress
        public Dictionary<string, double> PregnancyProgress = new Dictionary<string, double>();

        public override void OnAwake()
        {
            Instance = this;
        }

        public override void OnSave(ConfigNode node)
        {
            // Save Settings
            node.AddValue("BreedingEnabled", BreedingEnabled);
            node.AddValue("GenderMode", GenderMode);
            node.AddValue("BreedingSpeed", BreedingSpeed);
            node.AddValue("MaxConcurrentPregnancies", MaxConcurrentPregnancies);
            node.AddValue("ChildhoodDuration", ChildhoodDuration);

            // Save Family Tree
            ConfigNode treeNode = node.AddNode("FAMILY_TREE");
            foreach (var link in FamilyTree.Values)
            {
                ConfigNode entry = treeNode.AddNode("LINK");
                entry.AddValue("Child", link.ChildName);
                entry.AddValue("Father", link.FatherName);
                entry.AddValue("Mother", link.MotherName);
                entry.AddValue("BirthDate", link.BirthDate);
            }

            // Save Babies
            ConfigNode babiesNode = node.AddNode("NURSERY");
            foreach (var baby in GrowingBabies)
            {
                ConfigNode b = babiesNode.AddNode("BABY");
                b.AddValue("Name", baby.Key);
                b.AddValue("BirthDate", baby.Value);
            }

            // Save Pregnancies
            ConfigNode pregNode = node.AddNode("PREGNANCIES");
            foreach (var p in PregnancyProgress)
            {
                ConfigNode entry = pregNode.AddNode("MOM");
                entry.AddValue("Name", p.Key);
                entry.AddValue("Progress", p.Value);
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            FamilyTree.Clear();
            GrowingBabies.Clear();
            PregnancyProgress.Clear();

            // Load Settings
            if (node.HasValue("BreedingEnabled")) bool.TryParse(node.GetValue("BreedingEnabled"), out BreedingEnabled);
            if (node.HasValue("GenderMode")) int.TryParse(node.GetValue("GenderMode"), out GenderMode);
            if (node.HasValue("BreedingSpeed")) float.TryParse(node.GetValue("BreedingSpeed"), out BreedingSpeed);
            if (node.HasValue("MaxConcurrentPregnancies")) int.TryParse(node.GetValue("MaxConcurrentPregnancies"), out MaxConcurrentPregnancies);
            if (node.HasValue("ChildhoodDuration")) double.TryParse(node.GetValue("ChildhoodDuration"), out ChildhoodDuration);

            if (node.HasNode("FAMILY_TREE"))
            {
                foreach (ConfigNode entry in node.GetNode("FAMILY_TREE").GetNodes("LINK"))
                {
                    string c = entry.GetValue("Child");
                    string f = entry.GetValue("Father");
                    string m = entry.GetValue("Mother");
                    double d = double.Parse(entry.GetValue("BirthDate"));
                    FamilyTree[c] = new FamilyLink(c, f, m, d);
                }
            }

            if (node.HasNode("NURSERY"))
            {
                foreach (ConfigNode b in node.GetNode("NURSERY").GetNodes("BABY"))
                {
                    GrowingBabies[b.GetValue("Name")] = double.Parse(b.GetValue("BirthDate"));
                }
            }

            if (node.HasNode("PREGNANCIES"))
            {
                foreach (ConfigNode entry in node.GetNode("PREGNANCIES").GetNodes("MOM"))
                {
                    string n = entry.GetValue("Name");
                    double p = double.Parse(entry.GetValue("Progress"));
                    PregnancyProgress[n] = p;
                }
            }
        }
        
        // --- IMPROVED RELATIONSHIP CHECKING ---

        // Helper: Collects Parents and Grandparents into a set
        public HashSet<string> GetCloseRelatives(string name)
        {
            HashSet<string> relatives = new HashSet<string>();
            if (!FamilyTree.ContainsKey(name)) return relatives;

            FamilyLink link = FamilyTree[name];
            
            // Add Parents (Generation 1 Up)
            if (IsValidRelative(link.FatherName)) relatives.Add(link.FatherName);
            if (IsValidRelative(link.MotherName)) relatives.Add(link.MotherName);

            // Add Grandparents (Generation 2 Up)
            if (IsValidRelative(link.FatherName) && FamilyTree.ContainsKey(link.FatherName))
            {
                var dadLink = FamilyTree[link.FatherName];
                if (IsValidRelative(dadLink.FatherName)) relatives.Add(dadLink.FatherName);
                if (IsValidRelative(dadLink.MotherName)) relatives.Add(dadLink.MotherName);
            }
            if (IsValidRelative(link.MotherName) && FamilyTree.ContainsKey(link.MotherName))
            {
                var momLink = FamilyTree[link.MotherName];
                if (IsValidRelative(momLink.FatherName)) relatives.Add(momLink.FatherName);
                if (IsValidRelative(momLink.MotherName)) relatives.Add(momLink.MotherName);
            }

            return relatives;
        }

        private bool IsValidRelative(string n)
        {
            return !string.IsNullOrEmpty(n) && n != "Unknown";
        }

        public bool AreRelated(string nameA, string nameB)
        {
            // 0. Same Person
            if (nameA == nameB) return true; 

            // 1. Direct Parent Check (Immediate)
            if (FamilyTree.ContainsKey(nameB))
            {
                var link = FamilyTree[nameB];
                if (link.FatherName == nameA || link.MotherName == nameA) return true;
            }
            if (FamilyTree.ContainsKey(nameA))
            {
                var link = FamilyTree[nameA];
                if (link.FatherName == nameB || link.MotherName == nameB) return true;
            }

            // 2. Ancestry Intersection (Siblings, Half-Siblings, First Cousins, Aunt/Uncle)
            // We retrieve parents and grandparents for both. 
            // If the sets intersect at ALL, they share a recent blood relative.
            HashSet<string> relativesA = GetCloseRelatives(nameA);
            HashSet<string> relativesB = GetCloseRelatives(nameB);

            foreach(string r in relativesA)
            {
                if (relativesB.Contains(r)) return true; // Found a shared parent or grandparent
            }

            // 3. Direct Grandparent Check (Deep Lineage)
            // If A is in B's close relatives list (A is B's grandparent)
            if (relativesB.Contains(nameA)) return true;
            // If B is in A's close relatives list (B is A's grandparent)
            if (relativesA.Contains(nameB)) return true;

            return false;
        }

        public void HandleRenaming(string oldName, string newName)
        {
            if (FamilyTree.ContainsKey(oldName))
            {
                FamilyLink link = FamilyTree[oldName];
                link.ChildName = newName; 
                FamilyTree.Remove(oldName);
                FamilyTree.Add(newName, link);
            }

            foreach (var link in FamilyTree.Values)
            {
                if (link.FatherName == oldName) link.FatherName = newName;
                if (link.MotherName == oldName) link.MotherName = newName;
            }

            if (GrowingBabies.ContainsKey(oldName))
            {
                double birth = GrowingBabies[oldName];
                GrowingBabies.Remove(oldName);
                GrowingBabies.Add(newName, birth);
            }

            if (PregnancyProgress.ContainsKey(oldName))
            {
                double prog = PregnancyProgress[oldName];
                PregnancyProgress.Remove(oldName);
                PregnancyProgress.Add(newName, prog);
            }
        }
    }

    public class FamilyLink
    {
        public string ChildName;
        public string FatherName;
        public string MotherName;
        public double BirthDate;

        public FamilyLink(string c, string f, string m, double d)
        {
            ChildName = c; FatherName = f; MotherName = m; BirthDate = d;
        }
    }

    // ----------------------------------------------------------------------
    // 2. FLIGHT MANAGER: Handles Breeding, UI, and Renaming
    // ----------------------------------------------------------------------
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class GenerationsFlightManager : MonoBehaviour
    {
        // --- Settings ---
        private Rect _windowRect = new Rect(20, 100, 320, 580); 
        private bool _showWindow = false;
        private Vector2 _scrollPosition; 
        
        // Renaming State
        private bool _isRenaming = false;
        private ProtoCrewMember _kerbalToRename = null;
        private string _renameBuffer = "";

        // Status Text
        private string _breedingStatus = "Initializing...";

        // Local copies of settings
        private bool _breedingEnabled = true;
        private int _genderMode = 0;
        private float _breedingSpeed = 10.0f;
        private int _maxConcurrentPregnancies = 1; 
        private float _childhoodDays = 426.0f; 
        
        // State
        private const double BASE_GESTATION_SECONDS = 21600; 

        void Start()
        {
            if (GenerationsData.Instance != null)
            {
                _breedingEnabled = GenerationsData.Instance.BreedingEnabled;
                _genderMode = GenerationsData.Instance.GenderMode;
                _breedingSpeed = GenerationsData.Instance.BreedingSpeed;
                _maxConcurrentPregnancies = GenerationsData.Instance.MaxConcurrentPregnancies;
                _childhoodDays = (float)(GenerationsData.Instance.ChildhoodDuration / 21600.0);
            }
            CheckMaturation();
        }

        void Update()
        {
            if (FlightGlobals.ActiveVessel == null) return;
            
            if (Input.GetKeyDown(KeyCode.F7))
            {
                _showWindow = !_showWindow;
                if (!_showWindow) {
                    _isRenaming = false;
                    _kerbalToRename = null;
                    InputLockManager.RemoveControlLock("GenerationsRenameLock");
                }
            }

            if (!_breedingEnabled) {
                _breedingStatus = "Disabled in Settings";
                return;
            }

            if (Time.timeScale > 0)
            {
                ProcessBreeding(Time.deltaTime);
            }
            
            if (Time.frameCount % 60 == 0) 
            {
                CheckMaturation();
            }
        }

        private void ProcessBreeding(float deltaTime)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (GenerationsData.Instance == null) return;
            
            int totalSeats = v.GetCrewCapacity();
            int currentCrew = v.GetCrewCount();
            if ((totalSeats - currentCrew) < 2) {
                _breedingStatus = "Paused: Not enough room (Need 2+ empty seats)";
                return;
            }

            List<ProtoCrewMember> crew = v.GetVesselCrew();
            List<ProtoCrewMember> males = new List<ProtoCrewMember>();
            List<ProtoCrewMember> females = new List<ProtoCrewMember>();

            foreach(var c in crew) {
                if(c.type == ProtoCrewMember.KerbalType.Crew) {
                    if(c.gender == ProtoCrewMember.Gender.Male) males.Add(c);
                    if(c.gender == ProtoCrewMember.Gender.Female) females.Add(c);
                }
            }

            if (males.Count == 0) {
                _breedingStatus = "Paused: No adult males";
                return;
            }
            if (females.Count == 0) {
                _breedingStatus = "Paused: No adult females";
                return;
            }

            int currentShipPregnancies = 0;
            foreach (var mom in females)
            {
                if (GenerationsData.Instance.PregnancyProgress.ContainsKey(mom.name))
                    currentShipPregnancies++;
            }

            _breedingStatus = "Active"; 

            foreach(var mom in females)
            {
                bool isPregnant = GenerationsData.Instance.PregnancyProgress.ContainsKey(mom.name);

                if (isPregnant)
                {
                    GenerationsData.Instance.PregnancyProgress[mom.name] += deltaTime * _breedingSpeed;
                    
                    if (GenerationsData.Instance.PregnancyProgress[mom.name] >= BASE_GESTATION_SECONDS)
                    {
                        ProtoCrewMember dad = PickRandomDad(males, mom.name);
                        if (dad != null) {
                            SpawnBaby(v, mom, dad);
                            GenerationsData.Instance.PregnancyProgress.Remove(mom.name);
                        }
                    }
                    continue; 
                }

                if (currentShipPregnancies >= _maxConcurrentPregnancies) continue;

                ProtoCrewMember partner = PickRandomDad(males, mom.name);

                if (partner != null)
                {
                    GenerationsData.Instance.PregnancyProgress[mom.name] = 0.0;
                    currentShipPregnancies++; 
                }
            }
        }

        private ProtoCrewMember PickRandomDad(List<ProtoCrewMember> males, string momName)
        {
            // Retry logic: Try 10 times to find a non-related dad
            for (int i=0; i<10; i++) 
            {
                var potentialDad = males[UnityEngine.Random.Range(0, males.Count)];
                bool isRelated = GenerationsData.Instance.AreRelated(potentialDad.name, momName);
                
                if (!isRelated)
                {
                    return potentialDad;
                }
            }
            return null;
        }

        private void SpawnBaby(Vessel v, ProtoCrewMember mom, ProtoCrewMember dad)
        {
            ProtoCrewMember.Gender babyGender = ProtoCrewMember.Gender.Male;
            if (_genderMode == 1) babyGender = ProtoCrewMember.Gender.Male;
            else if (_genderMode == 2) babyGender = ProtoCrewMember.Gender.Female;
            else babyGender = UnityEngine.Random.value > 0.5f ? ProtoCrewMember.Gender.Male : ProtoCrewMember.Gender.Female;

            ProtoCrewMember baby = HighLogic.CurrentGame.CrewRoster.GetNewKerbal(ProtoCrewMember.KerbalType.Tourist);
            baby.gender = babyGender;
            
            Part emptyPart = null;
            foreach (Part p in v.parts)
            {
                if (p.CrewCapacity > p.protoModuleCrew.Count)
                {
                    emptyPart = p;
                    break;
                }
            }

            if (emptyPart != null)
            {
                bool success = emptyPart.AddCrewmember(baby);
                if (success)
                {
                    baby.rosterStatus = ProtoCrewMember.RosterStatus.Assigned;
                    
                    GameEvents.onVesselWasModified.Fire(v);
                    
                    ScreenMessages.PostScreenMessage($"A baby has been born to {dad.name} and {mom.name}! Welcome {baby.name}", 5.0f, ScreenMessageStyle.UPPER_CENTER);

                    if (GenerationsData.Instance != null)
                    {
                        GenerationsData.Instance.FamilyTree[baby.name] = new FamilyLink(baby.name, dad.name, mom.name, Planetarium.GetUniversalTime());
                        GenerationsData.Instance.GrowingBabies[baby.name] = Planetarium.GetUniversalTime();
                    }
                }
                else
                {
                    ScreenMessages.PostScreenMessage("Birth failed: Could not place baby in seat!", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            else
            {
                ScreenMessages.PostScreenMessage("Birth failed: No empty seats found!", 5.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void CheckMaturation()
        {
            if (GenerationsData.Instance == null) return;

            double now = Planetarium.GetUniversalTime();
            double requiredDuration = GenerationsData.Instance.ChildhoodDuration;
            
            List<string> grownUps = new List<string>();

            foreach (var kvp in GenerationsData.Instance.GrowingBabies)
            {
                string name = kvp.Key;
                double birthTime = kvp.Value;

                if ((now - birthTime) > requiredDuration)
                {
                    ProtoCrewMember k = HighLogic.CurrentGame.CrewRoster[name];
                    if (k != null && k.type == ProtoCrewMember.KerbalType.Tourist)
                    {
                        k.type = ProtoCrewMember.KerbalType.Crew;
                        float roll = UnityEngine.Random.value;
                        if (roll < 0.33f) k.trait = "Pilot";
                        else if (roll < 0.66f) k.trait = "Engineer";
                        else k.trait = "Scientist";
                        
                        ScreenMessages.PostScreenMessage(name + " has grown up and is now a " + k.trait + "!", 10.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                    grownUps.Add(name);
                }
            }

            foreach (string name in grownUps)
            {
                GenerationsData.Instance.GrowingBabies.Remove(name);
            }
        }

        // --- GUI Drawing ---
        void OnGUI()
        {
            if (!_showWindow) return;
            GUI.skin = HighLogic.Skin; 
            _windowRect = GUILayout.Window(8472, _windowRect, DrawWindow, "Kerbal Generations");
        }

        void DrawWindow(int windowID)
        {
            if (_isRenaming && _kerbalToRename != null)
            {
                GUILayout.BeginVertical();
                GUILayout.Label("Renaming: " + _kerbalToRename.name, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
                GUILayout.Space(10);
                
                GUILayout.Label("New Name:");
                _renameBuffer = GUILayout.TextField(_renameBuffer);

                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Save")) PerformRename();
                if (GUILayout.Button("Cancel"))
                {
                    _isRenaming = false;
                    _kerbalToRename = null;
                    InputLockManager.RemoveControlLock("GenerationsRenameLock");
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUI.DragWindow();
                return;
            }


            GUILayout.BeginVertical();

            GUILayout.Label("Breeding Controls", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            
            GUIStyle statusStyle = new GUIStyle(GUI.skin.label);
            if (_breedingStatus.StartsWith("Paused")) statusStyle.normal.textColor = Color.red;
            else if (_breedingStatus == "Active") statusStyle.normal.textColor = Color.green;
            GUILayout.Label("Status: " + _breedingStatus, statusStyle);
            
            GUILayout.Space(5);

            bool newEnabled = GUILayout.Toggle(_breedingEnabled, "Breeding Enabled");
            if (newEnabled != _breedingEnabled)
            {
                _breedingEnabled = newEnabled;
                if (GenerationsData.Instance != null) GenerationsData.Instance.BreedingEnabled = _breedingEnabled;
            }

            if (_breedingEnabled)
            {
                GUILayout.Space(10);
                GUILayout.Label("Options:");
                
                int oldMode = _genderMode;
                if (GUILayout.Toggle(_genderMode == 0, "Random")) _genderMode = 0;
                if (GUILayout.Toggle(_genderMode == 1, "Males Only")) _genderMode = 1;
                if (GUILayout.Toggle(_genderMode == 2, "Females Only")) _genderMode = 2;

                if (_genderMode != oldMode && GenerationsData.Instance != null)
                {
                    GenerationsData.Instance.GenderMode = _genderMode;
                }

                GUILayout.Space(5);
                GUILayout.Label($"Max Simultaneous Pregnancies: {_maxConcurrentPregnancies}");
                float newLimit = GUILayout.HorizontalSlider(_maxConcurrentPregnancies, 1.0f, 10.0f);
                if ((int)newLimit != _maxConcurrentPregnancies)
                {
                    _maxConcurrentPregnancies = (int)newLimit;
                    if (GenerationsData.Instance != null) GenerationsData.Instance.MaxConcurrentPregnancies = _maxConcurrentPregnancies;
                }

                GUILayout.Space(10);
                GUILayout.Label($"Breeding Speed: {_breedingSpeed:F1}x");
                float newSpeed = GUILayout.HorizontalSlider(_breedingSpeed, 0.1f, 100.0f);
                if (newSpeed != _breedingSpeed)
                {
                    _breedingSpeed = newSpeed;
                    if (GenerationsData.Instance != null) GenerationsData.Instance.BreedingSpeed = _breedingSpeed;
                }

                GUILayout.Space(10);
                GUILayout.Label($"Childhood Duration: {_childhoodDays:F1} Days");
                float newDays = GUILayout.HorizontalSlider(_childhoodDays, 0f, 426f);
                if (newDays != _childhoodDays)
                {
                    _childhoodDays = newDays;
                    if (GenerationsData.Instance != null) 
                    {
                        GenerationsData.Instance.ChildhoodDuration = _childhoodDays * 21600.0;
                    }
                }

                GUILayout.Space(10);
                
                if (GenerationsData.Instance != null && GenerationsData.Instance.PregnancyProgress.Count > 0)
                {
                    GUILayout.Label("Expecting Mothers:", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
                    
                    List<string> activeMoms = new List<string>(GenerationsData.Instance.PregnancyProgress.Keys);
                    foreach(string momName in activeMoms)
                    {
                        double prog = GenerationsData.Instance.PregnancyProgress[momName];
                        float pct = (float)(prog / BASE_GESTATION_SECONDS * 100);
                        GUILayout.Label($"- {momName}: {pct:F0}%");
                    }
                }
                else
                {
                     GUILayout.Label("No active pregnancies.");
                }

                GUILayout.Space(10);
                Vessel v = FlightGlobals.ActiveVessel;
                if (v != null)
                {
                    int total = v.GetCrewCapacity();
                    int current = v.GetCrewCount();
                    GUILayout.Label($"Seats: {current}/{total} (Free: {total-current})");
                }
            }

            GUILayout.Space(20);
            GUILayout.Label("Family Log & Renaming", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(200)); 
            
            if (FlightGlobals.ActiveVessel != null)
            {
                foreach (var crew in FlightGlobals.ActiveVessel.GetVesselCrew())
                {
                    GUILayout.BeginHorizontal();
                    
                    if (GUILayout.Button("R", GUILayout.Width(25)))
                    {
                        StartRenaming(crew);
                    }

                    GUILayout.BeginVertical();
                    GUILayout.Label(crew.name, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
                    
                    if (GenerationsData.Instance != null && GenerationsData.Instance.FamilyTree.ContainsKey(crew.name))
                    {
                        var link = GenerationsData.Instance.FamilyTree[crew.name];
                        GUILayout.Label($"  F: {link.FatherName} | M: {link.MotherName}", new GUIStyle(GUI.skin.label) { fontSize = 10 });
                    }
                    GUILayout.EndVertical();

                    GUILayout.EndHorizontal();
                    GUILayout.Space(5); 
                }
            }
            
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void StartRenaming(ProtoCrewMember crew)
        {
            _isRenaming = true;
            _kerbalToRename = crew;
            _renameBuffer = crew.name;
            InputLockManager.SetControlLock(ControlTypes.KEYBOARDINPUT, "GenerationsRenameLock");
        }

        private void PerformRename()
        {
            if (_kerbalToRename == null) return;
            string oldName = _kerbalToRename.name;
            string newName = _renameBuffer.Trim();

            if (string.IsNullOrEmpty(newName)) return;
            if (HighLogic.CurrentGame.CrewRoster.Exists(newName) && newName != oldName)
            {
                ScreenMessages.PostScreenMessage("Error: Name already exists!", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            _kerbalToRename.ChangeName(newName);

            if (GenerationsData.Instance != null)
            {
                GenerationsData.Instance.HandleRenaming(oldName, newName);
            }

            _isRenaming = false;
            _kerbalToRename = null;
            InputLockManager.RemoveControlLock("GenerationsRenameLock");
        }
    }
}