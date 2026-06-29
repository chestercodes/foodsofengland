---
agent: speckit.specify
---

i want an app to extract information from a website that has been saved to disk by a website scraper. the website is a history of foods, it contains pages which are about foods including recipes, with images of the food. it also contains posts about other historical topics involving food, but might not include recipes. the website was probably a wordpress site and sometimes the pages contains links to places which don't exist or contain other errors
the website was written by someone who is not a developer and the files are not well organized, so the app needs to be able to handle a variety of file types and structures. the same html pages and images might be present at the root of the folder and also in subfolders, so the app needs to be able to look through all the files in the folder and its subfolders. the app should be able to handle a large number of files, so it should be efficient and not use too much memory. the app should also be able to handle errors gracefully, such as when it encounters a file that it cannot parse or when it encounters a duplicate file name. the app should log any errors that it encounters for later inspection.

it should be able to perform a sequence of steps

## step 1: identify files
it needs to look at a folder on disk and do a basic look at the files, identifying
- the file hash of the file, this will be used to identify duplicates
- whether file contains html, this is probably best determined by the file extension having "htm" in it. if a html file:
- - want to parse out the title tag
- - if the tag is "page not found" then the file should not go to the next step
- if not html, then identify any pictures
- if not either then store an error detailing that the file is not an image or page and do not process to the next step

the first step should save the results of the identify files step in a sqlite table with the relative path to the file as a key, it should store the file hash, file type (html or image) and it should store the title tag as an optional field for html files and an error field for files that are not html or images

it should print out the duplicates at the end of the first step

## step 2: parse html files

for the files that were identified as html in step 1, it should try and find the content of the page. most of the html files were produced from one of three templates, but there might be other instances where it was not produced from a template, so the app should be able to handle both cases.

each template contains a body of content and the divider between these are specified with by starting and ending with comments, or maybe ending with the end of the file. the comments for the start of the body content are "<!---TEXT START--->", "<!---BODY TEXT START--->" or "<!--PAGE CONTENT-->" and end with "<!---TEXT END--->", "<!---BODY TEXT END--->" or the end of the file.

once it has found the content of each page it should store it in a new table for step 2 with the relative path to the file as a key and a column for the content of the page.

then it should parse out any urls which are in the files. each url should be stored in a separate table with the relative path to the file as a key and columns for the url, tag type (e.g. a, img, etc), and any alt text if available. if it appears to be a relative path, it should try and figure out if the file exists on disk and store this as a nullable bool, where true means that the file definitely exists on disk, false means that it definitely does not exist on disk, and null means that it is not clear whether the file exists on disk or it means that it is a url to an external domain. the domains of the site are www.foodsofengland.co.uk, foodsofengland.co.uk

it shouldn't assume too much about the html because it was written by a non developer.

## step 3: final file names

for the html files, the better value for the name is the title tag, since this will likely be more accurate than the file name. for each of the html files it needs to take the title, remove any special characters and then kebabify it to create the final name for the file. it should be easy to transform this file and then it should look for duplicates of the final name and if there are any, take the first one and log the rest as errors to be inspected later, it should then allow for a way for overrides to be specified to fix the duplicates
it should save these final in a column in a new table with the relative path to the file as a key and a column for the final name and file type (html or image or other).

for the images, it should look at the parsed url table and initially see if the image path is referenced in only one html file, if it is then it should take the final html name for the file determined above as a prefix and then add the original image file name, kebabified with non standard chars stripped out. if the image is referenced by multiple html files, then it should keep it's original name but kebabify it and strip out non standard chars, it should then look for duplicates and if there are any, take the first one and log the rest as errors to be inspected later, it should then allow for a way for overrides to be specified to fix the duplicates
it should save these final names in the same table as the html files with the relative path to the file as a key and a column for the final name and file type (html or image or other).

## step 4: pre-tidy rendering

once the final names have been decided, each html file content should be rendered to a working directory with the final name as the file name with html as an extension. 
the urls should be transformed in the html content to point to the final names of the files that were determined in step 3.
it should also be easy to make other regex based transformations to the content at the step.
this step is a precursor to running the tidy console application on the files which will be done in step 5, the tidy console application will be used to clean up the html and make it more consistent, but it can be a bit aggressive in some of the transformations it makes so this step allows for some pre-tidy transformations to be made to make sure that the tidy app doesn't mess up the content too much.
it should look for lines that contain the string "scroll.png" and replace it with "scroll.png" id="<guid>" to give each image a unique guid which can be used to pull out recipes in the next step. 

## step 5: tidy console application

the tidy console application should be run on each of the files that were rendered in step 4. this will clean up the html and make it more consistent.

the results should be output to file and then saved to a new table with the final file name as a key, and columns for the exit code, stdout and stderr of the tidy console application. if there are any errors (i.e. non zero exit code) then these should be logged for inspection later.

## step 6: information extraction

after the tidy console application has been run, there should be a post tidy run step to do some transformations to the content, mostly to remove html tags.
then a function should be run to extract tags from each of the html files. tags should be saved into a new table 
the html might contain recipes, each page can have multiple in it, or it may not be a food page, and will not have recipes in it. the start line of a recipe will have a line that contains <img src="scroll.png" 
and normally ends with a line that contains a hr tag, the end of one recipe line might be the start of the next recipe
this step should try and pull out recipes and put them into a new table with a column for the lines of the recipe text and should include the guid added to the img tag in step 4.

