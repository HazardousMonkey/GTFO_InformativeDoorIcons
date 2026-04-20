## Informative Door Icons -- Client-side

<p align="center">This mod adds visual QoL to the various types of door icons. Color changes, sprite replacements, label enhancements, bug fixes, etc. Don't like what you're seeing? Toggle or change most features via the Gale/R2 config editors, even during gameplay.</p>

<p align="center">
	<a href="https://i.ibb.co/tWrGzGS/GTFO-IDI-Info-v5.png">
		<img src="https://i.ibb.co/tWrGzGS/GTFO-IDI-Info-v5.png" title="Non-alarm Sec Doors have a unique color profile, Security doors now have their sprite color matched to the key that unlocks them, Security Doors display known info about themselves on the map, Weak Doors have a unique color when affected by lock or when foamed, 'APEX' doors will now visually appear as 'ALARM' doors, Bulkhead doors are now far more obvious due to a sprite overhaul and some bug fixes, and XYZ_DOOR_123 titles are no longer several pixels deep into the door sprite."/>
	</a>
</p>

<p align="center">There's also some miscellaneous options you can check out in the configs that improve map clarity.</p>
<p align="center">
	<a href="https://i.ibb.co/F4VprH4Z/image.png">
		<img src="https://i.ibb.co/F4VprH4Z/image.png" title="Misc features"/>
	</a>
</p>

<p align="center">
	<img src="https://i.ibb.co/ZztJ3FT9/Arrow-Blue-128px.png" alt="Love ya"/>
</p>

<p align="center">Info for Rundown devs: The Extra Door Info option draws the names for Generators, Bulkhead_DCs, Keycards, and Alarm classifications from these sources:</p>

- LG_SecurityDoor_Locks.m_powerGeneratorNeeded.`m_itemKey`
- LG_SecurityDoor_Locks.m_bulkheadDCNeeded.`PublicName`
- LG_SecurityDoor.m_keyItem.`PublicName`
- LG_SecurityDoor_Locks.ChainedPuzzleToSolve.Data.`PublicAlarmName`

The Security Door color-to-key matching system also depends on the LG_SecurityDoor.m_keyItem.`m_keyName` string following the normal `KEY_COLOR_ID` format. It won't work without it.
