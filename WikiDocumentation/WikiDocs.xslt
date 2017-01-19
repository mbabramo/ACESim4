<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" 
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:msxsl="urn:schemas-microsoft-com:xslt" 
    exclude-result-prefixes="msxsl">
    
    <!-- TODO when an XML comment is badly formed, a message such as the following is thrown. -->
    <!-- Badly formed XML comment ignored for member "M:ACESim.GamePlayer.Play(System.Collections.Generic.List{ACESim.Genome},System.Int32,System.Boolean,System.Int32,ACESim.SimulationSettings[])" -->
    
    <xsl:output method="text" indent="no"/>
    <xsl:variable name="newline" select="'&#xa;'"/>
    
    <!-- There's a replace(string, toReplace, replaceWith) function in XSLT 2.0, but neither msxsl.exe nor .NET's System.Xml.Xsl support 2.0.  http://saxon.sourceforge.net/ is an alternative. -->
    <xsl:template name="string-replace-all">
        <xsl:param name="text"/>
        <xsl:param name="replace"/>
        <xsl:param name="by"/>
        <xsl:choose>
            <xsl:when test="contains($text, $replace)">
                <xsl:value-of select="substring-before($text,$replace)"/>
                <xsl:value-of select="$by" />
                <xsl:call-template name="string-replace-all">
                    <xsl:with-param name="text" select="substring-after($text,$replace)" />
                    <xsl:with-param name="replace" select="$replace" />
                    <xsl:with-param name="by" select="$by" />
                </xsl:call-template>
            </xsl:when>
            <xsl:otherwise>
                <xsl:value-of select="$text"/>
            </xsl:otherwise>
        </xsl:choose>
    </xsl:template>

    <xsl:template match="/doc">
        <xsl:apply-templates select="members/member[starts-with(@name,'T:')]" mode="Type">
            <xsl:sort select="@name"/>
        </xsl:apply-templates>
    </xsl:template>

    <xsl:template match="member" mode="Type">
        <xsl:variable name="className" select="substring-after(@name, 'T:')"/>
        <xsl:value-of select="concat('== ', $className, ' ==', $newline)"/>
        <xsl:value-of select="concat(normalize-space(summary), $newline)"/>
        
        <xsl:variable name="fieldPrefix" select="concat('F:', $className, '.')"/>
        <xsl:if test="../member[starts-with(@name, $fieldPrefix)]">
            <xsl:text>=== Fields ===</xsl:text>
            <xsl:value-of select="$newline"/>
            <xsl:apply-templates select="../member[starts-with(@name, $fieldPrefix )]" mode="NonType">
                <xsl:sort select="@name"/>
                <xsl:with-param name="className" select="$className"/>
            </xsl:apply-templates>
        </xsl:if>
        
        <xsl:variable name="propertyPrefix" select="concat('P:', $className, '.')"/>
        <xsl:if test="../member[starts-with(@name, $propertyPrefix)]">
            <xsl:text>=== Properties ===</xsl:text>
            <xsl:value-of select="$newline"/>
            <xsl:apply-templates select="../member[starts-with(@name, $propertyPrefix)]" mode="NonType">
                <xsl:sort select="@name"/>
                <xsl:with-param name="className" select="$className"/>
            </xsl:apply-templates>
        </xsl:if>
        
        <xsl:variable name="methodPrefix" select="concat('M:', $className, '.')"/>
        <xsl:if test="../member[starts-with(@name, $methodPrefix)]">
            <xsl:text>=== Methods ===</xsl:text>
            <xsl:value-of select="$newline"/>
            <xsl:apply-templates select="../member[starts-with(@name, $methodPrefix)]" mode="NonType">
                <xsl:sort select="@name"/>
                <xsl:with-param name="className" select="$className"/>
            </xsl:apply-templates>
        </xsl:if>
        
        <xsl:variable name="typePrefix" select="concat('T:', $className, '.')"/>
        <xsl:if test="../member[starts-with(@name, $typePrefix)]">
            <xsl:text>=== Types ===</xsl:text>
            <xsl:value-of select="$newline"/>
            <xsl:apply-templates select="../member[starts-with(@name, $typePrefix)]" mode="Type">
                <xsl:sort select="@name"/>
                <xsl:with-param name="className" select="$className"/>
            </xsl:apply-templates>
        </xsl:if>
        
        <xsl:value-of select="$newline"/>
        <xsl:value-of select="$newline"/>
    </xsl:template>

    <xsl:template match="member" mode="NonType">
        <xsl:param name="className"/>
        
        <!-- remove the class name... -->
        <xsl:variable name="memberNameWithBraces" select="substring-after(@name, concat($className, '.'))"/>
        <!-- ... then replace { and } with < and >  -->
        <!-- replace is only available in xslt 2.0 -->
        <!-- ><xsl:variable name="memberName" select="replace(replace($memberNameWithBraces, '{', '&lt;'), '}', '&gt;')"/> -->
        <xsl:variable name="memberNameWithRightBraces">
            <xsl:call-template name="string-replace-all">
                <xsl:with-param name="text" select="$memberNameWithBraces" />
                <xsl:with-param name="replace" select="'{'" />
                <xsl:with-param name="by" select="'&lt;'" />
            </xsl:call-template>
        </xsl:variable>
        <xsl:variable name="memberName">
            <xsl:call-template name="string-replace-all">
                <xsl:with-param name="text" select="$memberNameWithRightBraces" />
                <xsl:with-param name="replace" select="'}'" />
                <xsl:with-param name="by" select="'&gt;'" />
            </xsl:call-template>
        </xsl:variable>

        
        <!-- if this method has a value or returns element with a cref element, use that as a type -->
        <xsl:variable name="typeInfo">
            <xsl:variable name="prefixedType">
                <xsl:choose>
                    <xsl:when test="value/@cref">
                        <xsl:value-of select="value/@cref"/>
                    </xsl:when>
                    <xsl:when test="returns/@cref">
                        <xsl:value-of select="returns/@cref"/>
                    </xsl:when>
                </xsl:choose>
            </xsl:variable>
            <xsl:if test="string-length($prefixedType)!=0">
                <!-- prefixedType is prefixed with T: if the type is recognized, !: otherwise -->
                <!-- First index is 1, so to start with the third character (prefix is two chars long), use 3 -->
                <xsl:variable name="returnType" select="substring($prefixedType, 3)"/>
                <xsl:value-of select="concat(' as ', $returnType)"/>
            </xsl:if>
        </xsl:variable>
        
        <xsl:value-of select="concat(&quot; '''&quot;, $memberName, &quot;'''&quot;, $typeInfo, ':: ')"/>
        
        <xsl:if test="summary">
            <xsl:apply-templates select="summary/* | summary/text()"/>
            <xsl:value-of select="$newline"/>
        </xsl:if>
        
        <xsl:if test="remarks">
            <xsl:value-of select="' * Remarks: '"/>
            <xsl:apply-templates select="remarks/* | remarks/text()"/>
            <xsl:value-of select="$newline"/>
        </xsl:if>
        
        <xsl:if test="example">
            <xsl:apply-templates select="example"/>
        </xsl:if>
        
        <xsl:if test="param">
            <xsl:apply-templates select="param"/>
        </xsl:if>
        
        <xsl:if test="typeparam">
            <xsl:apply-templates select="typeparam"/>
        </xsl:if>
        
        <xsl:if test="returns">
            <xsl:value-of select="' * Returns: '"/>
            <xsl:apply-templates select="returns/* | returns/text()"/>
            <xsl:value-of select="$newline"/>
        </xsl:if>
        
        <xsl:if test="exception">
            <xsl:apply-templates select="exception"/>
        </xsl:if>
        
        <xsl:if test="permission">
            <xsl:apply-templates select="permission"/>
        </xsl:if>
        
        <xsl:if test="seealso">
            <xsl:apply-templates select="seealso"/>
        </xsl:if>
        
        <xsl:value-of select="$newline"/>
    </xsl:template>
    
    <xsl:template match="example">
        <xsl:variable name="normalizedText">
            <xsl:value-of select="* | text()"/>
        </xsl:variable>
        <xsl:value-of select="concat(' * Example: ', $normalizedText)"/>
        <xsl:value-of select="$newline"/>
    </xsl:template>
    
    <xsl:template match="param">
        <xsl:variable name="normalizedText">
            <xsl:value-of select="* | text()"/>
        </xsl:variable>
        <xsl:value-of select="concat(' 1. ', @name, ': ', $normalizedText)"/>
        <xsl:value-of select="$newline"/>
    </xsl:template>
    
    <xsl:template match="typeparam">
        <xsl:variable name="normalizedText">
            <xsl:value-of select="* | text()"/>
        </xsl:variable>
        <xsl:value-of select="concat(' 1. Type Param: ', @name, ': ', $normalizedText)"/>
        <xsl:value-of select="$newline"/>
    </xsl:template>
    
    <xsl:template match="exception">
        <xsl:variable name="normalizedText">
            <xsl:value-of select="* | text()"/>
        </xsl:variable>
        <xsl:value-of select="concat(' * Throws: ', substring-after(@cref, 'T:'), ': ', $normalizedText)"/>
        <xsl:value-of select="$newline"/>
    </xsl:template>
    
    <xsl:template match="permission">
        <xsl:variable name="normalizedText">
            <xsl:value-of select="* | text()"/>
        </xsl:variable>
        <xsl:value-of select="concat(' * Requires Permission To: ', $normalizedText)"/>
        <xsl:value-of select="$newline"/>
    </xsl:template>
    
    <xsl:template match="seealso">
        <xsl:value-of select="concat(' * See Also: ', @cref)"/>
        <xsl:value-of select="$newline"/>
    </xsl:template>
    
    <xsl:template match="text()">
        <!-- This removes the leading and trailing spaces from the text(), so inline tags no longer have a space between them and the text. -->
        <!-- <xsl:value-of select="normalize-space(.)"/> -->
        <!-- So just return the text unmodified.  Might be nice to at least replace newlines with spaces. -->
        <!-- Actually what I want to do here is check if there is a leading space(s), whether there is a trailing space(s), normalize the text, and then add back a leading/trailing space (to preserve space between a tag before/after.) -->
        <xsl:value-of select="."/>
    </xsl:template>
    
    <xsl:template match="para">
        <xsl:value-of select="* | text()"/>
        <xsl:value-of select="'[[br]][[br]]'"/>
    </xsl:template>
    
    <xsl:template match="see">
        <xsl:value-of select="concat('See: ', @cref)"/>
    </xsl:template>
    
    <xsl:template match="paramref">
        <xsl:value-of select="@name"/>
    </xsl:template>
    
    <xsl:template match="list">
        <!-- TODO -->
        <xsl:value-of select="text()"/>
    </xsl:template>
    
    <xsl:template match="a">
        <xsl:value-of select="concat('[', @href, ' ', text(), ']')"/>
    </xsl:template>
    
    <xsl:template match="code">
        <xsl:value-of select="concat('{{{', text(), '}}}')"/>
    </xsl:template>
    
    <xsl:template match="c">
        <xsl:value-of select="concat('`', text(), '`')"/>
    </xsl:template>
    
    <xsl:template match="see">
        <xsl:value-of select="concat('See: ', text())"/>
    </xsl:template>
</xsl:stylesheet>
