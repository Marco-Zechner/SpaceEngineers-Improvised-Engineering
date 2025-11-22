----
If you publish your mod then the published version (you have to download it again from steam)
Should get a metadata.mod file (not used, so ignore it)
And it should get a "modinfo.sbmi" file, which is used by steam to know which mod is your mod, so you can update it instead of creating a new mod again and again.
It should contain this below with your the 2 numbers entered.

<?xml version="1.0"?>
<MyObjectBuilder_ModInfo xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <SteamIDOwner>YourSteamID</SteamIDOwner>
  <WorkshopId>YouritemID</WorkshopId>
</MyObjectBuilder_ModInfo>


----
If you want to Rename your mod rename 
- the parent Folder (which holds this README.txt)