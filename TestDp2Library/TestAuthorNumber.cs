﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using DigitalPlatform.LibraryServer;
using DigitalPlatform.Text;

namespace TestDp2Library
{
    [TestClass]
    public class TestAuthorNumber
    {
        [TestMethod]
        public void Test_GetSubRange()
        {
            string gcat_xml = @"<i h='陈' p='CHEN2' >

            string case_xml = @"<root>

        <r n='A' v='C350' f='10' />
</root>";

            XmlDocument gcat_dom = new XmlDocument();
            gcat_dom.LoadXml(gcat_xml);

            XmlDocument case_dom = new XmlDocument();
            case_dom.LoadXml(case_xml);

            XmlNodeList nodes = case_dom.DocumentElement.SelectNodes("r");
            foreach(XmlElement node in nodes)
            {
                string strName = node.GetAttribute("n");
                string strValue = node.GetAttribute("v");
                string strFufen = node.GetAttribute("f");

                List<string> parts = StringUtil.ParseTwoPart(strName, "-");
                foreach (string strPinyin in parts)
                {
                    if (string.IsNullOrEmpty(strPinyin))
                        continue;
                    // parameters:
                    //		strPinyin	一个汉字的拼音。如果==""，表示找第一个r元素
                    // return:
                    //		-1	出错
                    //		0	没有找到
                    //		1	找到
                    int nRet = LibraryApplication.GetSubRange(gcat_dom,
                        strPinyin,
                        false, // bool bOutputDebugInfo,
                        out string strOutputValue,
                        out string strOutputFufen,
                        out string strDebugInfo,
                        out string strError);
                    if (nRet == -1)
                        throw new Exception($"拼音 {strPinyin} 获得范围时出错: {strError} ");
                    // 比较返回结果
                    if (strOutputValue != strValue)
                        throw new Exception($"拼音 {strPinyin} 获得范围时出错: 返回value='{strOutputValue}' 和期望值 '{strValue}' 不同 ");
                    if (strOutputFufen != strFufen)
                        throw new Exception($"拼音 {strPinyin} 获得范围时出错: 返回fufen='{strOutputFufen}' 和期望值 '{strFufen}' 不同 ");
                }
            }
        }
    }
}