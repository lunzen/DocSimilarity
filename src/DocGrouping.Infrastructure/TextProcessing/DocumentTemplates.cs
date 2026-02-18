namespace DocGrouping.Infrastructure.TextProcessing;

public record TemplateDefinition(
	string TemplateId,
	string Name,
	string Category,
	string Description,
	string Content,
	Dictionary<string, string> DefaultVariables);

public static class DocumentTemplates
{
	public static readonly List<TemplateDefinition> All =
	[
		new("lease_agreement", "Lease Agreement", "Contracts",
			"Oil and gas lease agreement with working interest and royalty provisions",
			"""
			OIL AND GAS LEASE

			This Oil and Gas Lease ("Lease") is entered into effective as of {{date}}, by and between {{lessor}} ("Lessor"), and {{lessee}} ("Lessee").

			RECITALS

			WHEREAS, Lessor is the owner of certain oil, gas, and mineral interests in the property described below; and
			WHEREAS, Lessee desires to explore for, develop, produce, and market oil, gas, and other hydrocarbons from said property.

			NOW, THEREFORE, in consideration of the mutual covenants and agreements contained herein, the parties agree as follows:

			ARTICLE I - GRANT

			Lessor hereby grants, leases, and lets exclusively unto Lessee the land described below for the purpose of investigating, exploring, prospecting, drilling, and mining for and producing oil, gas, and all other hydrocarbons, together with the right to make surveys, lay pipelines, build tanks, power stations, and structures necessary to produce, save, care for, treat, transport, and own said products.

			Property Description: {{property_description}}

			ARTICLE II - TERM

			This Lease shall remain in force for a primary term of {{primary_term}} years from the date hereof and as long thereafter as oil, gas, or other hydrocarbon is produced from said land or land with which said land is pooled.

			ARTICLE III - ROYALTY

			Lessee covenants and agrees:
			(a) To deliver to the credit of Lessor, free of cost, in the pipelines to which Lessee may connect wells on said land, the equal one-eighth part of all oil produced and saved from said land.
			(b) To pay Lessor on gas and casinghead gas produced from any well, while such gas is sold or used, one-eighth of the market value at the well of such production.

			Working Interest: {{working_interest}}%
			Net Revenue Interest: {{net_revenue_interest}}%

			ARTICLE IV - DELAY RENTALS

			If operations for drilling are not commenced on said land on or before one year from this date, this Lease shall terminate unless Lessee on or before that date shall pay or tender to Lessor the sum of ${{delay_rental}} per acre for the next succeeding twelve months.

			ARTICLE V - OPERATIONS AND DEVELOPMENT

			Lessee shall conduct all operations in a good and workmanlike manner with due diligence and dispatch and in accordance with the customs and practices of the industry.

			ARTICLE VI - POOLING AND UNITIZATION

			Lessee shall have the right to pool or combine the acreage covered by this Lease with other land in the immediate vicinity for the purpose of creating drilling or proration units.

			IN WITNESS WHEREOF, the parties have executed this Lease as of the date first written above.

			LESSOR: {{lessor}}
			By: _______________________
			Title: {{lessor_title}}

			LESSEE: {{lessee}}
			By: _______________________
			Title: {{lessee_title}}
			""",
			new()
			{
				["date"] = "March 15, 2023",
				["lessor"] = "Western Mineral Rights, LLC",
				["lessee"] = "Permian Resources Corporation",
				["property_description"] = "Section 14, Township 2 South, Range 3 East, Reeves County, Texas, containing 640 acres, more or less",
				["primary_term"] = "5",
				["working_interest"] = "87.5",
				["net_revenue_interest"] = "75.0",
				["delay_rental"] = "10.00",
				["lessor_title"] = "Managing Member",
				["lessee_title"] = "Vice President, Land",
			}),

		new("assignment_agreement", "Assignment Agreement", "Contracts",
			"Assignment and conveyance of oil and gas lease interests",
			"""
			ASSIGNMENT AND CONVEYANCE OF OIL AND GAS LEASE

			This Assignment and Conveyance ("Assignment") is entered into effective as of {{effective_date}}, by and between {{assignor}} ("Assignor"), and {{assignee}} ("Assignee").

			RECITALS

			WHEREAS, Assignor is the owner of certain oil, gas, and mineral leasehold interests; and
			WHEREAS, Assignee desires to acquire said interests from Assignor.

			NOW, THEREFORE, for and in consideration of {{consideration}} and other good and valuable consideration, the receipt and sufficiency of which are hereby acknowledged, Assignor hereby GRANTS, BARGAINS, SELLS, ASSIGNS, TRANSFERS, and CONVEYS unto Assignee the following:

			PROPERTY ASSIGNED

			All of Assignor's right, title, and interest in and to the oil and gas leases described below:

			{{property_description}}

			The Leases cover approximately {{total_acres}} net mineral acres.

			ARTICLE I - ASSIGNMENT

			Assignor assigns to Assignee an undivided {{percentage_interest}}% interest in and to the Leases, including:
			(a) All working interest, net revenue interest, and other interests in the Leases;
			(b) All wells, equipment, facilities, and personal property;
			(c) All production accruing on and after the Effective Date;
			(d) All contracts, agreements, and records relating to the Leases.

			ARTICLE II - CONSIDERATION

			The total consideration for this Assignment is {{consideration}}, payable as follows:
			- Cash payment at closing: ${{cash_payment}}
			- Assumption of obligations: ${{assumed_obligations}}

			ARTICLE III - WARRANTIES

			Assignor warrants that it has good and marketable title to the interests being assigned, free and clear of all liens, encumbrances, and adverse claims.

			IN WITNESS WHEREOF, the parties have executed this Assignment as of the date first written above.

			ASSIGNOR: {{assignor}}
			By: _______________________
			Name: {{assignor_representative}}
			Title: {{assignor_title}}

			ASSIGNEE: {{assignee}}
			By: _______________________
			Name: {{assignee_representative}}
			Title: {{assignee_title}}
			""",
			new()
			{
				["effective_date"] = "January 1, 2023",
				["assignor"] = "Pioneer Energy Partners, LLC",
				["assignee"] = "Meridian Resources Corporation",
				["consideration"] = "Five Million Dollars ($5,000,000.00)",
				["property_description"] = "Oil and Gas Lease dated March 10, 2020, recorded in Volume 892, Page 445, covering Section 22, Block 45, T-2-S, Howard County, Texas",
				["total_acres"] = "1,280",
				["percentage_interest"] = "100",
				["cash_payment"] = "4,500,000.00",
				["assumed_obligations"] = "500,000.00",
				["assignor_representative"] = "John Smith",
				["assignor_title"] = "President",
				["assignee_representative"] = "Jane Doe",
				["assignee_title"] = "Vice President, Acquisitions",
			}),

		new("environmental_report", "Environmental Report", "Reports",
			"Phase I environmental site assessment report",
			"""
			PHASE I ENVIRONMENTAL SITE ASSESSMENT

			Property: {{property_name}}
			Location: {{property_location}}
			Date of Assessment: {{assessment_date}}
			Prepared for: {{client_name}}
			Prepared by: {{consulting_firm}}
			Project Number: {{project_number}}

			EXECUTIVE SUMMARY

			This Phase I Environmental Site Assessment (ESA) was conducted in accordance with ASTM Standard E1527-13 to identify recognized environmental conditions (RECs) at the subject property.

			PROPERTY INFORMATION

			Property Address: {{property_location}}
			Property Size: {{property_size}} acres
			Current Use: {{current_use}}

			SCOPE OF SERVICES

			1. Site reconnaissance and visual observations
			2. Interviews with property owners and occupants
			3. Review of historical sources and aerial photographs
			4. Regulatory database search and review
			5. Review of previous environmental reports

			SITE RECONNAISSANCE FINDINGS

			Date of Site Visit: {{site_visit_date}}
			Weather Conditions: {{weather_conditions}}

			Observations:
			- Site topography: {{topography_description}}
			- Visible staining or odors: {{staining_observations}}
			- Storage tanks: {{tank_observations}}

			REGULATORY DATABASE REVIEW

			- National Priorities List (NPL): No listings within 1 mile
			- RCRA TSD Facilities: {{rcra_findings}}
			- LUST database: {{lust_findings}}

			CONCLUSIONS AND RECOMMENDATIONS

			{{rec_summary}}

			Recommendations:
			1. {{recommendation_1}}
			2. {{recommendation_2}}

			Environmental Consultant: {{inspector_name}}
			License Number: {{license_number}}
			Date: {{report_date}}
			""",
			new()
			{
				["property_name"] = "Riverside Industrial Complex",
				["property_location"] = "1250 Industrial Parkway, Houston, Harris County, Texas 77001",
				["assessment_date"] = "October 15, 2023",
				["client_name"] = "Longhorn Development Partners, LLC",
				["consulting_firm"] = "EnviroTech Consulting Services",
				["project_number"] = "2023-1847-ESA",
				["property_size"] = "15.5",
				["current_use"] = "Former manufacturing facility, currently vacant",
				["site_visit_date"] = "October 10, 2023",
				["weather_conditions"] = "Clear, 72F",
				["topography_description"] = "Generally flat with slight slope to the south",
				["staining_observations"] = "Minor staining observed in former loading dock area",
				["tank_observations"] = "No above-ground storage tanks observed",
				["rcra_findings"] = "One RCRA Small Quantity Generator identified 0.3 miles northwest",
				["lust_findings"] = "Three LUST sites within 0.5 miles, all with closure status",
				["rec_summary"] = "One REC was identified: soil staining in the former loading dock area suggests potential hydrocarbon releases.",
				["recommendation_1"] = "Conduct soil sampling in the loading dock area",
				["recommendation_2"] = "Review documentation for 1998 underground storage tank closure",
				["inspector_name"] = "David Thompson, P.G.",
				["license_number"] = "TX-2847",
				["report_date"] = "October 15, 2023",
			}),

		new("production_report", "Production Report", "Reports",
			"Monthly oil and gas production report with well performance data",
			"""
			MONTHLY PRODUCTION REPORT

			Operator: {{operator_name}}
			Report Period: {{report_period}}
			Field/Area: {{field_name}}
			County: {{county}}, State: {{state}}
			Report Date: {{report_date}}

			EXECUTIVE SUMMARY

			This report summarizes oil, gas, and water production for the month of {{report_month}} {{report_year}}. Total production from {{total_wells}} active wells resulted in {{total_oil}} barrels of oil, {{total_gas}} MCF of natural gas, and {{total_water}} barrels of water.

			WELL PRODUCTION SUMMARY

			Well Name: {{well_name}}
			API Number: {{api_number}}
			Lease: {{lease_name}}

			Production Data for {{report_month}} {{report_year}}:
			- Days Online: {{days_online}} days
			- Oil Production: {{oil_production}} bbls ({{oil_daily_avg}} bbls/day)
			- Gas Production: {{gas_production}} MCF ({{gas_daily_avg}} MCF/day)
			- Water Production: {{water_production}} bbls
			- Gas-Oil Ratio: {{gor}} MCF/bbl
			- Water Cut: {{water_cut}}%

			RESERVOIR AND COMPLETION DATA

			Formation: {{formation_name}}
			Total Depth: {{total_depth}} feet
			Completion Type: {{completion_type}}
			Artificial Lift: {{artificial_lift}}

			PERFORMANCE ANALYSIS

			The well showed {{performance_trend}} performance compared to the previous month.
			- Production Efficiency: {{production_efficiency}}%
			- Flowing Tubing Pressure: {{flowing_pressure}} psi
			- Reservoir Pressure (estimated): {{reservoir_pressure}} psi

			RECOMMENDATIONS

			1. {{recommendation_1}}
			2. {{recommendation_2}}

			Prepared by: {{preparer_name}}
			Title: {{preparer_title}}
			Date: {{preparation_date}}
			""",
			new()
			{
				["operator_name"] = "Eagle Ford Operating Company",
				["report_period"] = "October 2023",
				["field_name"] = "Cedar Creek Field",
				["county"] = "Karnes",
				["state"] = "Texas",
				["report_date"] = "November 5, 2023",
				["report_month"] = "October",
				["report_year"] = "2023",
				["total_wells"] = "12",
				["total_oil"] = "45,673",
				["total_gas"] = "128,450",
				["total_water"] = "12,890",
				["well_name"] = "Cedar Creek Unit 1H",
				["api_number"] = "42-255-38904",
				["lease_name"] = "Mitchell Ranch Unit",
				["days_online"] = "31",
				["oil_production"] = "4,247",
				["oil_daily_avg"] = "137",
				["gas_production"] = "11,583",
				["gas_daily_avg"] = "374",
				["water_production"] = "892",
				["gor"] = "2.73",
				["water_cut"] = "17.4",
				["formation_name"] = "Eagle Ford Shale",
				["total_depth"] = "12,456",
				["completion_type"] = "Horizontal with hydraulic fracturing (22 stages)",
				["artificial_lift"] = "Electric Submersible Pump (ESP)",
				["performance_trend"] = "stable to slightly declining",
				["production_efficiency"] = "94.2",
				["flowing_pressure"] = "1,240",
				["reservoir_pressure"] = "4,250",
				["recommendation_1"] = "Continue current ESP operating parameters",
				["recommendation_2"] = "Schedule comprehensive wellbore integrity test in Q1 2024",
				["preparer_name"] = "Michael Anderson",
				["preparer_title"] = "Production Engineer",
				["preparation_date"] = "November 3, 2023",
			}),

		new("title_opinion", "Title Opinion", "Legal",
			"Attorney's opinion on oil and gas mineral title",
			"""
			TITLE OPINION

			Opinion Number: {{opinion_number}}
			Date: {{opinion_date}}
			Client: {{client_name}}
			Property: {{property_description}}

			TO: {{client_name}}
			RE: Title Opinion for Oil, Gas, and Mineral Interests

			Dear {{client_representative}}:

			INTRODUCTION

			At your request, I have examined the title to the oil, gas, and mineral interests in and under the land described below. This opinion is based upon an examination of the records in the {{county}} County Clerk's Office.

			PROPERTY DESCRIPTION

			{{property_description}}
			County: {{county}}, State: {{state}}
			Containing approximately {{total_acres}} acres, more or less.

			CURRENT OWNERSHIP

			{{current_owner}}: {{ownership_percentage}}% undivided interest in the oil, gas, and minerals.

			CHAIN OF TITLE SUMMARY

			1. {{chain_entry_1}}
			2. {{chain_entry_2}}
			3. {{chain_entry_3}}

			TITLE DEFECTS AND REQUIREMENTS

			REQUIREMENT 1: {{requirement_1}}
			Recommendation: {{recommendation_1}}

			REQUIREMENT 2: {{requirement_2}}
			Recommendation: {{recommendation_2}}

			CONCLUSION AND OPINION

			Subject to the title defects above, it is my opinion that {{current_owner}} owns {{ownership_percentage}}% of the oil, gas, and mineral interests and that said ownership is marketable.

			Respectfully submitted,

			{{examiner_name}}
			{{examiner_title}}
			State Bar Number: {{bar_number}}
			Date: {{opinion_date}}
			""",
			new()
			{
				["opinion_number"] = "TO-2023-1456",
				["opinion_date"] = "September 20, 2023",
				["client_name"] = "Permian Acquisition Partners, LLC",
				["property_description"] = "Section 18, Block 32, T-5-S, R-40-E, Township Survey, Lea County, New Mexico",
				["client_representative"] = "Ms. Sarah Johnson",
				["county"] = "Lea",
				["state"] = "New Mexico",
				["total_acres"] = "640",
				["current_owner"] = "Stockton Mineral Holdings, LLC",
				["ownership_percentage"] = "100",
				["chain_entry_1"] = "Patent from the United States of America to John H. Stewart, dated July 15, 1922",
				["chain_entry_2"] = "Warranty Deed from John H. Stewart to Stewart Ranch Company, dated March 3, 1955",
				["chain_entry_3"] = "Mineral Deed from Stewart Ranch Company to Delaware Basin Resources, Inc., dated June 22, 1998",
				["requirement_1"] = "The Warranty Deed contains an indefinite legal description that should be clarified",
				["recommendation_1"] = "Obtain a corrective instrument clarifying the legal description",
				["requirement_2"] = "Recorded Assignment dated January 10, 2019 omits Exhibit A",
				["recommendation_2"] = "File a corrective assignment with proper exhibit attached",
				["examiner_name"] = "Robert Williams, Esq.",
				["examiner_title"] = "Attorney at Law, Petroleum Landman (CPL)",
				["bar_number"] = "NM-34876",
			}),

		new("right_of_way", "Right of Way Agreement", "Contracts",
			"Easement agreement for pipeline or roadway construction",
			"""
			RIGHT OF WAY AND EASEMENT AGREEMENT

			This Right of Way and Easement Agreement ("Agreement") is entered into on {{agreement_date}}, by and between {{grantor}} ("Grantor"), and {{grantee}} ("Grantee").

			WHEREAS, Grantor is the owner of certain real property described herein; and
			WHEREAS, Grantee desires to obtain a right-of-way and easement for {{easement_purpose}}.

			Grantor hereby grants to Grantee a perpetual right-of-way and easement {{easement_width}} feet in width across:

			{{property_description}}

			PURPOSE: Laying, constructing, installing, operating, maintaining, and repairing {{facility_description}}.

			CONSTRUCTION: Grantee shall minimize interference, restore the surface, and comply with all applicable laws.
			Construction Period: {{construction_start_date}} to {{construction_completion_date}}.

			CONSIDERATION: ${{easement_payment}} per rod, total ${{total_payment}}.

			LIABILITY: Grantee shall indemnify Grantor against claims arising from Grantee's activities.

			INSURANCE: Minimum limits of ${{insurance_amount}} per occurrence.

			IN WITNESS WHEREOF, the parties have executed this Agreement.

			GRANTOR: {{grantor}}
			By: _______________________
			Name: {{grantor_representative}}

			GRANTEE: {{grantee}}
			By: _______________________
			Name: {{grantee_representative}}
			""",
			new()
			{
				["agreement_date"] = "April 18, 2023",
				["grantor"] = "Riverside Ranch, LLC",
				["grantee"] = "TransTexas Pipeline Company",
				["easement_purpose"] = "a natural gas pipeline",
				["easement_width"] = "50",
				["property_description"] = "A portion of Section 9, Block 14, T-3-N, Anderson County, Texas",
				["facility_description"] = "one 16-inch diameter natural gas pipeline and all associated equipment",
				["construction_start_date"] = "June 1, 2023",
				["construction_completion_date"] = "September 30, 2023",
				["easement_payment"] = "50.00",
				["total_payment"] = "25,000.00",
				["insurance_amount"] = "5,000,000",
				["grantor_representative"] = "Thomas Henderson",
				["grantee_representative"] = "Patricia Walker",
			}),

		new("well_completion_report", "Well Completion Report", "Reports",
			"Detailed report of well drilling and completion operations",
			"""
			WELL COMPLETION REPORT

			Well Name: {{well_name}}
			API Number: {{api_number}}
			Operator: {{operator_name}}
			County: {{county}}, State: {{state}}
			Field: {{field_name}}

			WELL INFORMATION

			Surface Location: {{surface_location}}
			Well Type: {{well_type}}
			Spud Date: {{spud_date}}
			Total Depth: {{total_depth}} feet MD ({{total_vertical_depth}} feet TVD)
			Total Drilling Days: {{total_days}} days
			Completion Date: {{completion_date}}

			GEOLOGICAL SUMMARY

			Target Formation: {{target_formation}}
			Formation Top: {{formation_top}} feet MD
			Porosity: {{porosity}}%
			Reservoir Pressure: {{reservoir_pressure}} psi

			COMPLETION DESIGN

			Completion Type: {{completion_type}}
			Number of Fracture Stages: {{frac_stages}}
			Total Proppant Placed: {{total_proppant}} pounds
			Total Fluid Pumped: {{total_fluid}} barrels

			PRODUCTION TESTING

			Test Date: {{test_date}}
			- Oil Rate: {{test_oil}} barrels per day
			- Gas Rate: {{test_gas}} MCF per day
			- Water Rate: {{test_water}} barrels per day
			- Gas-Oil Ratio: {{test_gor}} MCF/bbl

			COSTS

			Total Well Cost: ${{total_cost}}
			- Drilling: ${{drilling_cost}}
			- Completion: ${{completion_cost}}

			CONCLUSIONS

			1. {{conclusion_1}}
			2. {{conclusion_2}}

			Prepared by: {{preparer_name}}
			Title: {{preparer_title}}
			""",
			new()
			{
				["well_name"] = "Wildcat Energy 24-15H",
				["api_number"] = "42-389-41256",
				["operator_name"] = "Wildcat Energy Partners, LLC",
				["county"] = "Midland",
				["state"] = "Texas",
				["field_name"] = "Spraberry Trend Area",
				["surface_location"] = "Section 24, Block 39, T-1-S, T&P RR Co. Survey",
				["well_type"] = "Horizontal",
				["spud_date"] = "March 1, 2023",
				["total_depth"] = "18,456",
				["total_vertical_depth"] = "9,280",
				["total_days"] = "42",
				["completion_date"] = "May 10, 2023",
				["target_formation"] = "Wolfcamp A",
				["formation_top"] = "9,847",
				["porosity"] = "8.5",
				["reservoir_pressure"] = "5,450",
				["completion_type"] = "Multi-stage hydraulic fracturing with plug-and-perf",
				["frac_stages"] = "32",
				["total_proppant"] = "18,500,000",
				["total_fluid"] = "425,000",
				["test_date"] = "May 12, 2023",
				["test_oil"] = "1,450",
				["test_gas"] = "4,250",
				["test_water"] = "125",
				["test_gor"] = "2.93",
				["total_cost"] = "8,750,000",
				["drilling_cost"] = "3,250,000",
				["completion_cost"] = "4,500,000",
				["conclusion_1"] = "Well successfully drilled and completed; initial production exceeds estimates",
				["conclusion_2"] = "Recommend similar completion design for future wells",
				["preparer_name"] = "James Wilson, P.E.",
				["preparer_title"] = "Completion Engineer",
			}),

		new("surface_use_agreement", "Surface Use Agreement", "Contracts",
			"Agreement for surface access and operations on private land",
			"""
			SURFACE USE AND DAMAGE AGREEMENT

			This Surface Use and Damage Agreement ("Agreement") is entered into on {{agreement_date}}, by and between {{surface_owner}} ("Surface Owner"), and {{operator}} ("Operator").

			PROPERTY DESCRIPTION

			{{property_description}}
			Total Surface Acreage: {{total_acreage}} acres

			PERMITTED ACTIVITIES

			Operator may conduct geological surveys, drill and maintain wells, construct roads and pipelines, and transport equipment across the Property.

			SURFACE DISTURBANCE

			Well Pad Location: {{well_pad_location}}
			Well Pad Size: Not to exceed {{well_pad_size}} acres

			SURFACE USE PAYMENT

			(a) Well Pad Payment: ${{well_pad_payment}} per acre
			(b) Annual Surface Rental: ${{surface_rental}} per year
			Total Initial Payment: ${{initial_total}}

			DAMAGE COMPENSATION

			Cultivated Cropland: ${{cropland_rate}} per acre per year
			Improved Pasture: ${{pasture_rate}} per acre per year

			RESTORATION

			Upon completion, Operator shall remove equipment, fill excavations, restore drainage, and reseed with native grass blend. Restoration within {{restoration_timeframe}} days.

			ENVIRONMENTAL PROTECTION

			Operator shall comply with environmental laws, prevent spills, dispose of waste off-site, and notify Surface Owner within {{spill_notification}} hours of any release.

			IN WITNESS WHEREOF, the parties have executed this Agreement.

			SURFACE OWNER: {{surface_owner}}
			Name: {{surface_owner_rep}}

			OPERATOR: {{operator}}
			Name: {{operator_rep}}
			Title: {{operator_title}}
			""",
			new()
			{
				["agreement_date"] = "June 5, 2023",
				["surface_owner"] = "Blackland Farms, Inc.",
				["operator"] = "Summit Energy Operating Company",
				["property_description"] = "Section 27, Block 8, H&TC RR Co. Survey, Gonzales County, Texas",
				["total_acreage"] = "485",
				["well_pad_location"] = "Northwest corner of Section 27",
				["well_pad_size"] = "5",
				["well_pad_payment"] = "2,500",
				["surface_rental"] = "2,000",
				["initial_total"] = "13,325",
				["cropland_rate"] = "350",
				["pasture_rate"] = "150",
				["restoration_timeframe"] = "90",
				["spill_notification"] = "24",
				["surface_owner_rep"] = "Michael Blackland",
				["operator_rep"] = "Jennifer Martinez",
				["operator_title"] = "Land Manager",
			}),

		new("seismic_data_summary", "Seismic Data Summary", "Reports",
			"3D seismic survey acquisition and processing summary",
			"""
			SEISMIC DATA ACQUISITION AND PROCESSING SUMMARY

			Survey Name: {{survey_name}}
			Project Number: {{project_number}}
			Client: {{client_name}}
			Location: {{survey_location}}
			Report Date: {{report_date}}

			EXECUTIVE SUMMARY

			This report summarizes the acquisition, processing, and preliminary interpretation of 3D seismic data over the {{survey_name}} prospect area.

			Survey Parameters:
			- Total Area: {{survey_area}} square miles
			- Line Miles Acquired: {{line_miles}} miles
			- Bin Size: {{bin_size}} feet x {{bin_size}} feet
			- Acquisition Period: {{acquisition_start}} to {{acquisition_end}}

			SURVEY OBJECTIVES

			1. {{objective_1}}
			2. {{objective_2}}

			Target Formations:
			- Primary Target: {{primary_target}} ({{primary_depth}} feet)
			- Secondary Target: {{secondary_target}} ({{secondary_depth}} feet)

			ACQUISITION PARAMETERS

			Source Type: {{source_type}}
			Receiver Type: {{receiver_type}}
			Survey Type: {{survey_type}}
			Fold: {{fold}}-fold coverage

			DATA QUALITY

			Signal-to-Noise Ratio: {{signal_noise_ratio}} ({{snr_quality}})
			Data quality is rated as {{overall_quality}}.

			INTERPRETATION HIGHLIGHTS

			{{structural_interpretation}}

			CONCLUSIONS

			1. {{conclusion_1}}
			2. {{conclusion_2}}

			Prepared by: {{preparer_name}}
			Title: {{preparer_title}}
			""",
			new()
			{
				["survey_name"] = "Eagle Point 3D",
				["project_number"] = "SEI-2023-447",
				["client_name"] = "Frontier Exploration Company",
				["survey_location"] = "Northwestern Pecos County, Texas",
				["report_date"] = "August 25, 2023",
				["survey_area"] = "42.5",
				["line_miles"] = "1,247",
				["bin_size"] = "82.5",
				["acquisition_start"] = "April 10, 2023",
				["acquisition_end"] = "June 15, 2023",
				["objective_1"] = "Map the Wolfcamp and Bone Spring formations",
				["objective_2"] = "Identify structural features including faults",
				["primary_target"] = "Wolfcamp B Shale",
				["primary_depth"] = "9,500",
				["secondary_target"] = "Bone Spring Limestone",
				["secondary_depth"] = "8,200",
				["source_type"] = "Vibroseis",
				["receiver_type"] = "Single geophone",
				["survey_type"] = "Orthogonal 3D",
				["fold"] = "40",
				["signal_noise_ratio"] = "18 dB",
				["snr_quality"] = "Good to Excellent",
				["overall_quality"] = "Good to Excellent",
				["structural_interpretation"] = "Gently dipping monocline with northeast-trending normal faults of 50-120 feet throw.",
				["conclusion_1"] = "Survey successfully achieved all objectives with good data quality",
				["conclusion_2"] = "Multiple prospective drilling locations identified",
				["preparer_name"] = "Amanda Foster",
				["preparer_title"] = "Senior Geophysicist",
			}),

		new("regulatory_filing", "Regulatory Filing", "Legal",
			"Permit application for oil and gas drilling operations",
			"""
			APPLICATION FOR PERMIT TO DRILL

			Regulatory Agency: {{agency_name}}
			Application Number: {{application_number}}
			Filing Date: {{filing_date}}
			Operator: {{operator_name}}

			SECTION 1 - OPERATOR INFORMATION

			Legal Name: {{operator_name}}
			Operator Number: {{operator_number}}
			Contact Person: {{contact_person}}
			Phone: {{contact_phone}}

			SECTION 2 - WELL INFORMATION

			Proposed Well Name: {{well_name}}
			Well Type: {{well_type}}
			Drilling Objective: {{drilling_objective}}

			Surface Location: {{surface_location}}
			Bottom Hole Location: {{bottom_hole_location}}

			SECTION 3 - LEASE AND OWNERSHIP

			Lease Name: {{lease_name}}
			Mineral Owner: {{mineral_owner}}
			Surface Owner: {{surface_owner}}

			SECTION 4 - GEOLOGICAL INFORMATION

			Target Formation: {{target_formation}}
			Estimated Total Depth: {{estimated_depth}}

			SECTION 5 - DRILLING PLAN

			Estimated Spud Date: {{spud_date}}
			Drilling Rig: {{rig_name}}

			SECTION 6 - ENVIRONMENTAL AND SAFETY

			Drilling Waste Disposal Method: {{waste_disposal}}
			Spill Prevention Plan: {{spill_plan_status}}

			SECTION 7 - COMPLIANCE CERTIFICATIONS

			The operator certifies that all spacing regulations are met, surface owner has been notified dated {{surface_notification_date}}, and all bonds are current. Bond Number: {{bond_number}}, Amount: ${{bond_amount}}.

			SIGNATURE

			Operator: {{operator_name}}
			Name: {{signatory_name}}
			Title: {{signatory_title}}
			Date: {{signature_date}}
			""",
			new()
			{
				["agency_name"] = "Texas Railroad Commission",
				["application_number"] = "APD-2023-0789",
				["filing_date"] = "July 10, 2023",
				["operator_name"] = "Permian Basin Exploration, LLC",
				["operator_number"] = "123456",
				["contact_person"] = "Jennifer Collins, Regulatory Affairs Manager",
				["contact_phone"] = "(432) 555-0198",
				["well_name"] = "State Lease 47-22H",
				["well_type"] = "Horizontal Oil Well",
				["drilling_objective"] = "Wolfcamp A Formation",
				["surface_location"] = "Section 47, Block 22, T-3-S, Midland County, Texas",
				["bottom_hole_location"] = "Section 46, Block 22, T-3-S, Midland County, Texas",
				["lease_name"] = "State of Texas Lease #47-22",
				["mineral_owner"] = "State of Texas",
				["surface_owner"] = "State of Texas",
				["target_formation"] = "Wolfcamp A Shale",
				["estimated_depth"] = "10,500 feet measured depth",
				["spud_date"] = "August 15, 2023",
				["rig_name"] = "Rig #47",
				["waste_disposal"] = "Commercial disposal facility",
				["spill_plan_status"] = "SPCC Plan prepared and on file",
				["surface_notification_date"] = "June 25, 2023",
				["bond_number"] = "SB-123456-2023",
				["bond_amount"] = "250,000",
				["signatory_name"] = "Thomas Reynolds",
				["signatory_title"] = "Vice President, Operations",
				["signature_date"] = "July 10, 2023",
			}),
	];

	public static TemplateDefinition? GetById(string templateId)
		=> All.FirstOrDefault(t => t.TemplateId == templateId);

	public static List<TemplateDefinition> GetByCategory(string category)
		=> All.Where(t => t.Category == category).ToList();
}
