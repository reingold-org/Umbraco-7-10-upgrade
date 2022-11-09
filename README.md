# Umbraco 7-10 Split &middot; [![GitHub license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.md) [![!PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)]()


The Umbraco 7-10 split is a collection of tools created to automate various processes involved with updating older versions of Umbraco to the latest and greatest. 

## Getting started

- Check out the [presentation slides](presentation/7-10.pdf) 
- Obtain a fresh database backup to work against
- Prep Umbraco 7 environment
    - Empty Recycling bin
    - Install NestingContently https://github.com/nathanwoulfe/NestingContently
    - Create NestingContently datatype 'NestingContently'
    - Install Umbraco GodMode https://github.com/DanDiplo/Umbraco.GodMode/
    - Remove any unused Datatypes that are obsolete/removed in Umbraco 9/10
- Set up fresh Umbraco environments for upgrading the database:
    - *Note that when iterating/retrying upgrades the 'Umbraco.Core.ConfigurationStatus' app setting of Umbraco 7 & 8 environments will need to be reset to their starting values.*
    - Umbraco 8.1.6 - Note that per [this issue](https://github.com/umbraco/Umbraco-CMS/issues/12351) this might not be necessary. 
    - Umbraco 8.18.5
    - Umbraco 9.4
        - Install the NestingContently package
- Install the [scripts](scripts) into their respective Umbraco projects

## Database Upgrade Process

### Run Umbraco 7 scripts

- Umbraco 7 migration scripts- https://localhost:port/umbraco/surface/migration/{action}
- TrimContentVersions
- GetPublishedPages - save the output of this
- UpdateObsoleteDatatypes
- UpdateContentAndDataTypeNames
- ConvertArchetypes
- ConvertNupickerDataTypes
- CreateNupickerProperties
- CopyNupickerData
- UpdateContentTypes
- CopyArchetypeData
- RemoveArchetypesAndNupickers
- UpdatePickerAndVideoData

### Upgrade database to Umbraco 8.1.6
Simply run the Umbraco 8.1.6 project with the conection strings set to the database and let Umbraco do the upgrade.

### Run Umbraco 8.1.6 script
- SetElementTypes - sets block types as elements

### Upgrade database to Umbraco 8.18.5
Same process as upgrading to 8.1.6

### Run Umbraco 8.18.5 script
- Update the list of page ids with the output of the GetPublishedPages script run against Umbraco 7
- PublishContent

### Upgrade database to Umbraco 9.4 
Same process as upgrading to 8.18.5

### Upgrade database to Umbraco 10.x
Update the Umbraco NuGet packages to version 10.x
Run the project as before and let Umbraco upgrade the database.

At this point you're ready to port your solution over 

## Copyright & Licence

&copy; 2022 by Reingold Inc.

This is free software and is licensed under the [MIT License](LICENSE)